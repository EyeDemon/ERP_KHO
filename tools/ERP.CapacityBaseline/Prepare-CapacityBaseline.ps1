[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ConfigPath,
    [switch]$PrepareOnly
)

$ErrorActionPreference = 'Stop'

function Assert-CapacityConfig {
    param([hashtable]$Config)

    $required = @('SchemaVersion','RunId','DatabaseName','DatabaseMarkerType','DatabaseMarkerRunId',
        'TargetEnvironmentId','ApprovedEnvironmentId','ApiBaseUri','ApiEnvironmentMarker','ExpectedCommit',
        'VirtualUsers','WarmupMinutes','MeasurementMinutes','OwnerApproved','ExecutionEnabled','CleanupApproved')
    foreach ($key in $required) {
        if (-not $Config.ContainsKey($key)) { throw "Missing required capacity setting: $key" }
    }

    foreach ($key in @('SchemaVersion','VirtualUsers','WarmupMinutes','MeasurementMinutes')) {
        if ($Config[$key] -isnot [int]) { throw "Capacity setting must be an integer: $key" }
    }
    foreach ($key in @('OwnerApproved','ExecutionEnabled','CleanupApproved')) {
        if ($Config[$key] -isnot [bool]) { throw "Capacity setting must be Boolean: $key" }
    }
    foreach ($key in @('RunId','DatabaseName','DatabaseMarkerType','DatabaseMarkerRunId','TargetEnvironmentId',
            'ApprovedEnvironmentId','ApiBaseUri','ApiEnvironmentMarker','ExpectedCommit')) {
        if ($Config[$key] -isnot [string] -or [string]::IsNullOrWhiteSpace($Config[$key])) { throw "Capacity setting must be a non-empty string: $key" }
    }

    if ($Config.SchemaVersion -ne 1) { throw 'Unsupported capacity settings schema.' }
    if ($Config.RunId -notmatch '^[a-f0-9]{32}$') { throw 'Run ID must be 32 lowercase hexadecimal characters.' }
    if ($Config.DatabaseName -notmatch '^ERP_KHO_Capacity_[0-9]{8}_[0-9]{6}_[a-f0-9]{8}$') { throw 'Database target is outside the capacity allowlist.' }
    if (@('ERP_KHO','master','model','msdb','tempdb') -contains $Config.DatabaseName) { throw 'Database target is denied.' }
    if (-not $Config.DatabaseName.EndsWith("_$($Config.RunId.Substring(0,8))", [StringComparison]::Ordinal)) { throw 'Database name does not belong to the configured Run ID.' }
    $timestampText = $Config.DatabaseName.Substring('ERP_KHO_Capacity_'.Length, 15)
    $timestamp = [DateTime]::MinValue
    if (-not [DateTime]::TryParseExact($timestampText, 'yyyyMMdd_HHmmss', [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$timestamp)) { throw 'Database name contains an invalid UTC timestamp.' }
    if ($Config.DatabaseMarkerType -ne 'ERP_KHO_CAPACITY_BASELINE' -or $Config.DatabaseMarkerRunId -ne $Config.RunId) { throw 'Database ownership marker does not match the run.' }
    if ([string]::IsNullOrWhiteSpace($Config.TargetEnvironmentId) -or $Config.TargetEnvironmentId -ne $Config.ApprovedEnvironmentId) { throw 'Approved environment identity mismatch.' }
    if ($Config.TargetEnvironmentId -like 'REPLACE_*' -or $Config.ApiEnvironmentMarker -like 'REPLACE_*') { throw 'Placeholder target identity is not executable.' }

    $uri = [Uri]$Config.ApiBaseUri
    if (-not $uri.IsAbsoluteUri -or $uri.Scheme -ne 'https') { throw 'API target must be an absolute HTTPS URI.' }
    if ($uri.UserInfo -or $uri.Query -or $uri.Fragment) { throw 'API target must not contain credentials, query parameters or fragments.' }
    if ($Config.ExpectedCommit -notmatch '^[a-f0-9]{40}$') { throw 'Expected commit must be a full SHA-1 hash.' }
    if ([int]$Config.VirtualUsers -lt 1 -or [int]$Config.VirtualUsers -gt 10) { throw 'Virtual-user limit must be between 1 and 10.' }
    if ([int]$Config.WarmupMinutes -lt 0 -or [int]$Config.WarmupMinutes -gt 5) { throw 'Warm-up exceeds the reviewed limit.' }
    if ([int]$Config.MeasurementMinutes -lt 1 -or [int]$Config.MeasurementMinutes -gt 30) { throw 'Measurement duration exceeds the reviewed limit.' }
}

$resolved = (Resolve-Path -LiteralPath $ConfigPath).Path
$config = Import-PowerShellDataFile -LiteralPath $resolved
Assert-CapacityConfig -Config $config

if (-not $PrepareOnly) { throw 'Capacity execution is not implemented or authorized. Use -PrepareOnly for review.' }

$plan = [ordered]@{
    schemaVersion = 1
    status = 'PREPARED_NOT_AUTHORIZED'
    validationScope = 'OFFLINE_CONFIGURATION_ONLY'
    liveTargetVerified = $false
    executionAuthorized = $false
    runId = $config.RunId
    databaseName = $config.DatabaseName
    targetEnvironmentId = $config.TargetEnvironmentId
    expectedCommit = $config.ExpectedCommit
    virtualUsers = [int]$config.VirtualUsers
    warmupMinutes = [int]$config.WarmupMinutes
    measurementMinutes = [int]$config.MeasurementMinutes
    ownerApproved = [bool]$config.OwnerApproved
    executionEnabled = [bool]$config.ExecutionEnabled
}

$plan | ConvertTo-Json -Depth 3
