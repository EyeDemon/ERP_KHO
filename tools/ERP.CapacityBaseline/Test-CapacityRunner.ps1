$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot 'Invoke-CapacityBaseline.ps1'
$runId = 'abcdef0123456789abcdef0123456789'
$root = Join-Path $env:TEMP "erp-capacity-runner-$runId"
New-Item -ItemType Directory -Path $root -Force | Out-Null

function Write-Config([string]$Path, [hashtable]$Overrides=@{}) {
    $c=@{SchemaVersion=1;RunId=$runId;DatabaseName='ERP_KHO_Capacity_20260907_010203_abcdef01';DatabaseMarkerType='ERP_KHO_CAPACITY_BASELINE';DatabaseMarkerRunId=$runId;TargetEnvironmentId='isolated-01';ApprovedEnvironmentId='isolated-01';ApiBaseUri='https://capacity.invalid';ApiEnvironmentMarker='api-01';ExpectedCommit='204773c0bcadc736d2e5d433576ace7d8af40d70';Profile='Typical';VirtualUsers=3;WarmupMinutes=2;MeasurementMinutes=10;RequestTimeoutSeconds=10;MaximumRunMinutes=20;OwnerApproved=$false;ExecutionEnabled=$false;CleanupApproved=$false;ApprovalExpiresAtUtc=[DateTimeOffset]::UtcNow.AddHours(1).ToString('o');FixtureManifestPath=(Join-Path $root "$runId-fixtures.json");ResultDirectory=$root}
    foreach($k in $Overrides.Keys){$c[$k]=$Overrides[$k]}
    '@{' + [Environment]::NewLine + (($c.GetEnumerator()|ForEach-Object{$v=if($_.Value-is[bool]){if($_.Value){'$true'}else{'$false'}}elseif($_.Value-is[int]){$_.Value}else{"'$($_.Value)'"};" $($_.Key)=$v"})-join[Environment]::NewLine)+[Environment]::NewLine+'}'|Set-Content $Path -Encoding UTF8
}
function Invoke-Runner([hashtable]$Overrides=@{},[switch]$Execute,[switch]$Live,[string]$Adapter,[bool]$Pass=$true){$cfg=Join-Path $root ([Guid]::NewGuid().ToString('N')+'.psd1');Write-Config $cfg $Overrides;$args=@('-NoProfile','-File',$runner,'-ConfigPath',$cfg);if($Execute){$args+=@('-Execute','-AdapterPath',$Adapter);if(-not $Live){$args+='-TestMode'}}else{$args+='-ValidateOnly'};$old=$ErrorActionPreference;$ErrorActionPreference='Continue';try{$o=& powershell.exe @args 2>&1;$e=$LASTEXITCODE}finally{$ErrorActionPreference=$old};if(($e-eq 0)-ne$Pass){throw "Unexpected runner result: $o"};"PASS: exit=$e"}

$adapter=Join-Path $root 'adapter.ps1'
@'
# ERP_CAPACITY_OFFLINE_TEST_ADAPTER
function Get-CapacityApiEvidence($Config){[pscustomobject]@{runId=$(if($env:ERP_CAPACITY_MOCK_CASE-eq'api-run'){'0'*32}else{$Config.RunId});environmentId=$(if($env:ERP_CAPACITY_MOCK_CASE-eq'api-env'){'other'}else{$Config.ApprovedEnvironmentId});databaseName=$(if($env:ERP_CAPACITY_MOCK_CASE-eq'api-db'){'other'}else{$Config.DatabaseName});bindingDigest='binding-1';verifiedAtUtc=[DateTimeOffset]::UtcNow.ToString('o');deploymentCommit=$(if($env:ERP_CAPACITY_MOCK_CASE-eq'api-commit'){'0'*40}else{$Config.ExpectedCommit});environmentMarker=$Config.ApiEnvironmentMarker}}
function Get-CapacitySqlEvidence($Config){[pscustomobject]@{runId=$Config.RunId;environmentId=$Config.ApprovedEnvironmentId;databaseName=$Config.DatabaseName;bindingDigest=$(if($env:ERP_CAPACITY_MOCK_CASE-eq'digest'){'binding-2'}else{'binding-1'});verifiedAtUtc=[DateTimeOffset]::UtcNow.ToString('o');markerType=$Config.DatabaseMarkerType;markerRunId=$(if($env:ERP_CAPACITY_MOCK_CASE-eq'ownership'){'0'*32}else{$Config.RunId});dbNameVerified=$true}}
function Invoke-CapacitySeed($Config,$SqlEvidence){}
function Invoke-CapacityProcess($Config,$ScriptPath){if($env:ERP_CAPACITY_MOCK_CASE -eq 'timeout'){return [pscustomobject]@{ExitCode=124}};if($env:ERP_CAPACITY_MOCK_CASE -eq 'cancelled'){return [pscustomobject]@{ExitCode=130}};if($env:ERP_CAPACITY_MOCK_CASE -eq 'safety-stop'){return [pscustomobject]@{ExitCode=2}};[pscustomobject]@{ExitCode=0}}
function Get-CapacityCollectors($Config){[pscustomobject]@{cpu='unavailable';reason='mock-only'}}
function Invoke-CapacityReconciliation($Config,$SqlEvidence){[pscustomobject]@{Passed=($env:ERP_CAPACITY_MOCK_CASE -ne 'reconciliation')}}
'@|Set-Content $adapter -Encoding UTF8

Invoke-Runner
Invoke-Runner @{Profile='Peak';VirtualUsers=11} -Pass:$false
Invoke-Runner @{ApprovalExpiresAtUtc='2026-09-07 01:02:03';OwnerApproved=$true;ExecutionEnabled=$true} -Execute -Adapter $adapter -Pass:$false
Invoke-Runner @{ApprovalExpiresAtUtc=[DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('o');OwnerApproved=$true;ExecutionEnabled=$true} -Execute -Adapter $adapter -Pass:$false
Invoke-Runner @{OwnerApproved=$true;ExecutionEnabled=$true} -Execute -Adapter $adapter -Pass:$false
Invoke-Runner @{OwnerApproved=$true;ExecutionEnabled=$true} -Execute -Live -Adapter $adapter -Pass:$false
$env:ERP_CAPACITY_TEST_MODE='1'
try { Invoke-Runner @{OwnerApproved=$true;ExecutionEnabled=$true} -Execute -Adapter $adapter } finally { Remove-Item Env:ERP_CAPACITY_TEST_MODE -ErrorAction SilentlyContinue }

foreach($mockCase in @('timeout','cancelled','safety-stop','reconciliation','api-run','api-env','api-db','api-commit','digest','ownership')){$env:ERP_CAPACITY_TEST_MODE='1';$env:ERP_CAPACITY_MOCK_CASE=$mockCase;try{Invoke-Runner @{OwnerApproved=$true;ExecutionEnabled=$true} -Execute -Adapter $adapter -Pass:$false}finally{Remove-Item Env:ERP_CAPACITY_TEST_MODE,Env:ERP_CAPACITY_MOCK_CASE -ErrorAction SilentlyContinue}}

$implementation=Get-Content $runner -Raw
if($implementation -match 'continue-on-error|SkipVerification|Bypass'){throw 'Runner contains a verification bypass.'}
if($implementation -notmatch 'Resolve-Path -LiteralPath \$env:TEMP'){throw 'Runner does not canonicalize the isolated test root.'}
$workload=Get-Content (Join-Path $PSScriptRoot 'capacity-workload.js') -Raw
if($workload -match 'console\.log\(.+(token|password|cookie)'){throw 'Workload may log credentials.'}
if($workload -notmatch 'expiresAtMs - 30000'){throw 'Workload lacks proactive token-expiry handling.'}
if($workload -notmatch 'body\.token' -or $workload -notmatch 'body\.accessTokenExpiresAtUtc' -or $workload -notmatch "responseType: 'text'"){throw 'Workload does not match the login wire contract.'}
if($workload -notmatch 'INVALID_ZERO_SAMPLES' -or $workload -notmatch 'abortOnFail: true'){throw 'Workload lacks zero-sample or safety-stop handling.'}
if($workload -notmatch 'expectedRejection\?\.scenario' -or $workload -notmatch 'expectedRejection\?\.reasonCode'){throw 'Expected rejection lacks a pre-labelled scenario and reason.'}
if($workload -notmatch 'Number\.isFinite\(samples\)' -or $workload -notmatch 'samples > 0'){throw 'Workload does not reject missing, NaN or zero measurement samples.'}
$result=Get-Content (Join-Path $root "capacity-$runId-result.json") -Raw
if($result -match '(?i)password|accessToken|refreshToken|cookie|connectionString'){throw 'Partial result contains sensitive fields.'}
'PASS: token expiry handling and result redaction guards'

$installer=Join-Path $PSScriptRoot 'Install-K6Pinned.ps1'
Add-Type -AssemblyName System.IO.Compression
function New-ZipFixture([string]$Path,[string]$EntryName){$stream=[IO.File]::Create($Path);try{$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create);try{$entry=$zip.CreateEntry($EntryName);$writer=[IO.StreamWriter]::new($entry.Open());try{$writer.Write('fixture')}finally{$writer.Dispose()}}finally{$zip.Dispose()}}finally{$stream.Dispose()}}
function Invoke-InstallerFixture([string]$Archive,[string]$Hash,[bool]$Pass){$old=$ErrorActionPreference;$ErrorActionPreference='Continue';try{$o=& powershell.exe -NoProfile -File $installer -DestinationDirectory $root -VerifyFixture -FixtureArchive $Archive -FixtureExpectedSha256 $Hash 2>&1;$e=$LASTEXITCODE}finally{$ErrorActionPreference=$old};if(($e-eq 0)-ne$Pass){throw "Unexpected installer result: $o"}}
$safeZip=Join-Path $root 'safe.zip';New-ZipFixture $safeZip 'k6-v2.1.0-windows-amd64/k6.exe';$safeHash=(Get-FileHash $safeZip -Algorithm SHA256).Hash;Invoke-InstallerFixture $safeZip $safeHash $true;Invoke-InstallerFixture $safeZip ('0'*64) $false
$escapeZip=Join-Path $root 'escape.zip';New-ZipFixture $escapeZip '../escape.exe';$escapeHash=(Get-FileHash $escapeZip -Algorithm SHA256).Hash;Invoke-InstallerFixture $escapeZip $escapeHash $false
'PASS: installer checksum and ZIP traversal guards'
'All capacity runner offline tests passed. Live HTTP, SQL, process and collector calls: 0.'
if ((Split-Path $root -Leaf) -ne "erp-capacity-runner-$runId") { throw 'Mock cleanup ownership mismatch.' }
Remove-Item -LiteralPath $root -Recurse -Force
