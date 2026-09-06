$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'Prepare-CapacityBaseline.ps1'
$base = @{
    SchemaVersion=1; RunId='abcdef0123456789abcdef0123456789';
    DatabaseName='ERP_KHO_Capacity_20260907_010203_abcdef01'; DatabaseMarkerType='ERP_KHO_CAPACITY_BASELINE';
    DatabaseMarkerRunId='abcdef0123456789abcdef0123456789'; TargetEnvironmentId='isolated-review-01';
    ApprovedEnvironmentId='isolated-review-01'; ApiBaseUri='https://capacity.invalid'; ApiEnvironmentMarker='isolated-api-01';
    ExpectedCommit='8c823bf9b81651202b930be0038cec6d2435f105'; VirtualUsers=3; WarmupMinutes=2;
    MeasurementMinutes=10; OwnerApproved=$false; ExecutionEnabled=$false; CleanupApproved=$false
}

function Invoke-Case([hashtable]$Config, [bool]$ShouldPass, [string]$Name, [switch]$WithoutPrepareOnly) {
    $path = Join-Path $env:TEMP ("erp-capacity-{0}.psd1" -f [Guid]::NewGuid().ToString('N'))
    try {
        '@{' + [Environment]::NewLine + (($Config.GetEnumerator() | ForEach-Object {
            $value = if ($_.Value -is [bool]) { if ($_.Value) { '$true' } else { '$false' } } elseif ($_.Value -is [int]) { $_.Value } else { "'$($_.Value)'" }
            "    $($_.Key) = $value"
        }) -join [Environment]::NewLine) + [Environment]::NewLine + '}' | Set-Content -LiteralPath $path -Encoding UTF8
        $args = @('-NoProfile','-File',$script,'-ConfigPath',$path)
        if (-not $WithoutPrepareOnly) { $args += '-PrepareOnly' }
        $previousPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $output = & powershell.exe @args 2>&1
            $exitCode = $LASTEXITCODE
        } finally {
            $ErrorActionPreference = $previousPreference
        }
        $passed = $exitCode -eq 0
        if ($passed -ne $ShouldPass) { throw "Guard case '$Name' produced unexpected result: $output" }
        "PASS: $Name"
    } finally { Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue }
}

Invoke-Case $base $true 'valid review-only manifest'
$case = $base.Clone(); $case.Remove('RunId'); Invoke-Case $case $false 'missing configuration denied'
$case = $base.Clone(); $case.RunId='not-a-run-id'; Invoke-Case $case $false 'invalid run ID denied'
$case = $base.Clone(); $case.DatabaseName='ERP_KHO_Capacity_20260907_010203_00000000'; Invoke-Case $case $false 'database and run ID mismatch denied'
$case = $base.Clone(); $case.DatabaseName='ERP_KHO_Capacity_20261340_250000_abcdef01'; Invoke-Case $case $false 'invalid database timestamp denied'
$case = $base.Clone(); $case.DatabaseName='ERP_KHO'; Invoke-Case $case $false 'real database denied'
$case = $base.Clone(); $case.DatabaseName='master'; Invoke-Case $case $false 'system database denied'
$case = $base.Clone(); $case.DatabaseMarkerRunId='00000000000000000000000000000000'; Invoke-Case $case $false 'ownership mismatch denied'
$case = $base.Clone(); $case.ApprovedEnvironmentId='different-environment'; Invoke-Case $case $false 'environment mismatch denied'
$case = $base.Clone(); $case.ApiBaseUri='http://capacity.invalid'; Invoke-Case $case $false 'non-TLS API denied'
$case = $base.Clone(); $case.ApiBaseUri='https://user:secret@capacity.invalid'; Invoke-Case $case $false 'URI credentials denied'
$case = $base.Clone(); $case.ApiBaseUri='https://capacity.invalid?target=other'; Invoke-Case $case $false 'URI query denied'
$case = $base.Clone(); $case.ApiEnvironmentMarker=''; Invoke-Case $case $false 'empty API marker denied'
$case = $base.Clone(); $case.OwnerApproved='false'; Invoke-Case $case $false 'string Boolean denied'
$case = $base.Clone(); $case.VirtualUsers='3'; Invoke-Case $case $false 'string integer denied'
$case = $base.Clone(); $case.VirtualUsers=1; $case.WarmupMinutes=0; $case.MeasurementMinutes=1; Invoke-Case $case $true 'lower load boundaries accepted'
$case = $base.Clone(); $case.VirtualUsers=10; $case.WarmupMinutes=5; $case.MeasurementMinutes=30; Invoke-Case $case $true 'upper load boundaries accepted'
$case = $base.Clone(); $case.VirtualUsers=11; Invoke-Case $case $false 'load limit denied'
$case = $base.Clone(); $case.WarmupMinutes=6; Invoke-Case $case $false 'warm-up limit denied'
$case = $base.Clone(); $case.MeasurementMinutes=31; Invoke-Case $case $false 'duration limit denied'
Invoke-Case $base $false 'execution remains disabled' -WithoutPrepareOnly

$implementation = Get-Content -LiteralPath $script -Raw
if ($implementation -match 'Invoke-(WebRequest|RestMethod)|SqlConnection|BACKUP\s|RESTORE\s|DROP\s+DATABASE') {
    throw 'Preparation script contains a network, SQL or destructive execution primitive.'
}
'PASS: preparation implementation has no network, SQL, backup, restore or database-drop primitive'

'All capacity preparation guard tests passed. Network and SQL calls: 0.'
