[CmdletBinding(DefaultParameterSetName = 'Validate')]
param(
    [Parameter(Mandatory = $true)][string]$ConfigPath,
    [Parameter(ParameterSetName = 'Validate')][switch]$ValidateOnly,
    [Parameter(Mandatory = $true, ParameterSetName = 'Execute')][switch]$Execute,
    [Parameter(Mandatory = $true, ParameterSetName = 'Execute')][string]$AdapterPath,
    [Parameter(ParameterSetName = 'Execute')][switch]$TestMode
)

$ErrorActionPreference = 'Stop'
$scriptRoot = $PSScriptRoot
. (Join-Path $scriptRoot 'Prepare-CapacityBaseline.ps1') -ConfigPath $ConfigPath -PrepareOnly | Out-Null

function Assert-RunnerConfig([hashtable]$Config) {
    $required = @('Profile','RequestTimeoutSeconds','MaximumRunMinutes','ApprovalExpiresAtUtc',
        'FixtureManifestPath','ResultDirectory')
    foreach ($key in $required) {
        if (-not $Config.ContainsKey($key)) { throw "Missing runner setting: $key" }
    }
    if (@('Smoke','Typical','Busy','Peak','Contention') -notcontains $Config.Profile) { throw 'Unsupported capacity profile.' }
    $expectedVus = @{ Smoke=1; Typical=3; Busy=6; Peak=10; Contention=2 }
    if ($Config.VirtualUsers -ne $expectedVus[$Config.Profile]) { throw 'Profile virtual-user count does not match the reviewed development profile.' }
    if ($Config.RequestTimeoutSeconds -isnot [int] -or $Config.RequestTimeoutSeconds -lt 1 -or $Config.RequestTimeoutSeconds -gt 30) { throw 'Request timeout is outside the reviewed limit.' }
    if ($Config.MaximumRunMinutes -isnot [int] -or $Config.MaximumRunMinutes -lt 1 -or $Config.MaximumRunMinutes -gt 40) { throw 'Maximum run time is outside the reviewed limit.' }
    $expires = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($Config.ApprovalExpiresAtUtc, [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$expires) -or $expires.Offset -ne [TimeSpan]::Zero) { throw 'Approval expiry must be an explicit UTC timestamp.' }
    if ($Config.FixtureManifestPath -like 'REPLACE_*' -or $Config.ResultDirectory -like 'REPLACE_*') { throw 'Runner paths still contain placeholders.' }
    if (-not [IO.Path]::IsPathRooted($Config.FixtureManifestPath) -or -not [IO.Path]::IsPathRooted($Config.ResultDirectory)) { throw 'Runner paths must be absolute.' }
    if ($Config.FixtureManifestPath -notlike "*$($Config.RunId)*" -or $Config.ResultDirectory -notlike "*$($Config.RunId)*") { throw 'Runner paths must be scoped to the exact Run ID.' }
}

function Assert-Evidence([hashtable]$Config, $Api, $Sql) {
    foreach ($evidence in @($Api,$Sql)) {
        foreach ($name in @('runId','environmentId','databaseName','bindingDigest','verifiedAtUtc')) {
            if ([string]::IsNullOrWhiteSpace([string]$evidence.$name)) { throw "TARGET_VERIFICATION_BLOCKED: missing $name" }
        }
    }
    if ($Api.runId -ne $Config.RunId -or $Sql.runId -ne $Config.RunId -or
        $Api.environmentId -ne $Config.ApprovedEnvironmentId -or $Sql.environmentId -ne $Config.ApprovedEnvironmentId -or
        $Api.databaseName -ne $Config.DatabaseName -or $Sql.databaseName -ne $Config.DatabaseName -or
        $Api.bindingDigest -ne $Sql.bindingDigest) { throw 'TARGET_VERIFICATION_BLOCKED: API and SQL evidence do not identify the same owned target.' }
    if ($Api.deploymentCommit -ne $Config.ExpectedCommit -or $Api.environmentMarker -ne $Config.ApiEnvironmentMarker) { throw 'TARGET_VERIFICATION_BLOCKED: API deployment identity mismatch.' }
    if ($Sql.markerType -ne $Config.DatabaseMarkerType -or $Sql.markerRunId -ne $Config.RunId -or $Sql.dbNameVerified -ne $true) { throw 'TARGET_VERIFICATION_BLOCKED: SQL ownership evidence mismatch.' }
}

$config = Import-PowerShellDataFile -LiteralPath (Resolve-Path -LiteralPath $ConfigPath)
Assert-RunnerConfig $config

if (-not $Execute) {
    [ordered]@{ status='VALIDATED_OFFLINE_ONLY'; liveTargetVerified=$false; executionAuthorized=$false; profile=$config.Profile } | ConvertTo-Json
    exit 0
}

if (-not $TestMode) { throw 'CONTROLLED_LIVE_ADAPTER_NOT_IMPLEMENTED: live execution remains blocked before target access.' }
if ($env:ERP_CAPACITY_TEST_MODE -ne '1') { throw 'Test adapters are unavailable outside the explicit offline test process.' }
if (-not $config.OwnerApproved -or -not $config.ExecutionEnabled) { throw 'CAPACITY_EXECUTION_BLOCKED: owner approval and execution enablement are required.' }
if ([DateTimeOffset]::UtcNow -ge [DateTimeOffset]::Parse($config.ApprovalExpiresAtUtc)) { throw 'CAPACITY_EXECUTION_BLOCKED: approved execution window expired.' }

$adapterFile = Get-Item -LiteralPath $AdapterPath
$tempDirectory = Get-Item -LiteralPath $env:TEMP
$testDirectory = Get-Item -LiteralPath $adapterFile.DirectoryName
$expectedTestDirectory = "erp-capacity-runner-$($config.RunId)"
if ($adapterFile.PSIsContainer -or $testDirectory.Name -cne $expectedTestDirectory -or
    $null -eq $testDirectory.Parent -or $testDirectory.Parent.FullName -cne $tempDirectory.FullName -or
    (Get-Content -LiteralPath $adapterFile.FullName -Raw) -notmatch '^# ERP_CAPACITY_OFFLINE_TEST_ADAPTER') {
    throw 'Test adapter is outside the isolated offline-test boundary.'
}
$adapter = $adapterFile.FullName
. $adapter
foreach ($fn in @('Get-CapacityApiEvidence','Get-CapacitySqlEvidence','Invoke-CapacitySeed','Invoke-CapacityProcess','Get-CapacityCollectors','Invoke-CapacityReconciliation')) {
    if (-not (Get-Command $fn -CommandType Function -ErrorAction SilentlyContinue)) { throw "TARGET_VERIFICATION_BLOCKED: adapter lacks $fn" }
}

$partial = [ordered]@{ schemaVersion=1; runId=$config.RunId; status='STARTED'; startedAtUtc=[DateTimeOffset]::UtcNow.ToString('o'); liveTargetVerified=$false; executionAuthorized=$false }
try {
    $apiEvidence = Get-CapacityApiEvidence -Config $config
    $sqlEvidence = Get-CapacitySqlEvidence -Config $config
    Assert-Evidence $config $apiEvidence $sqlEvidence
    $partial.liveTargetVerified = $true
    $partial.executionAuthorized = $true
    Invoke-CapacitySeed -Config $config -SqlEvidence $sqlEvidence
    $processResult = Invoke-CapacityProcess -Config $config -ScriptPath (Join-Path $scriptRoot 'capacity-workload.js')
    if ($processResult.ExitCode -ne 0) { throw "CAPACITY_RUN_FAILED: k6 exit $($processResult.ExitCode)" }
    $collectors = Get-CapacityCollectors -Config $config
    $reconciliation = Invoke-CapacityReconciliation -Config $config -SqlEvidence $sqlEvidence
    if (-not $reconciliation.Passed) { throw 'CAPACITY_RECONCILIATION_FAILED' }
    $partial.status='COMPLETED_REQUIRES_REVIEW'; $partial.collectors=$collectors; $partial.reconciliation=$reconciliation
} catch {
    $partial.status='BLOCKED_OR_FAILED'; $partial.errorCode=($_.Exception.Message -split ':')[0]
    throw
} finally {
    $partial.finishedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
    $redactedResult = $partial | ConvertTo-Json -Depth 6
    New-Item -ItemType Directory -Path $config.ResultDirectory -Force | Out-Null
    $resultPath = Join-Path $config.ResultDirectory "capacity-$($config.RunId)-result.json"
    Set-Content -LiteralPath $resultPath -Value $redactedResult -Encoding UTF8
    Write-Output $redactedResult
}
