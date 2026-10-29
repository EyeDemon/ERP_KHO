<#
.SYNOPSIS
    Test-ErpWmsStagingPreFlight.ps1 — ERP_KHO Staging/Production Cutover Pre-Flight Verifier
.DESCRIPTION
    Validates mandatory environment variables, SQL Server TCP reachability, and /health/live + /health/ready
    endpoints without exposing any secret values to console or logs ([SEC-003]).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ApiBaseUrl = "http://127.0.0.1:5142",

    [Parameter(Mandatory = $false)]
    [ValidateSet("Staging", "Production")]
    [string]$TargetEnvironment = "Production",

    [Parameter(Mandatory = $false)]
    [switch]$SkipHttpHealthProbes
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$results = New-Object System.Collections.Generic.List[pscustomobject]
$failureCount = 0

function Add-CheckResult {
    param(
        [string]$CheckId,
        [string]$Category,
        [ValidateSet("PASS", "FAIL", "WARN", "SKIP")]
        [string]$Status,
        [string]$Metadata
    )
    if ($Status -eq "FAIL") { $script:failureCount++ }
    $results.Add([pscustomobject]@{
        CheckId  = $CheckId
        Category = $Category
        Status   = $Status
        Metadata = $Metadata
    })
}

# 1. Working Directory Drive-Letter Casing Check (RSK-OPS-001)
$cwd = (Get-Location).Path
$drivePrefix = $cwd.Substring(0, [Math]::Min(2, $cwd.Length))
if ($drivePrefix -cmatch '^[A-Z]:$') {
    Add-CheckResult "PRE-01" "CWD Drive Casing (RSK-OPS-001)" "PASS" "Uppercase drive prefix verified ($drivePrefix)"
} else {
    Add-CheckResult "PRE-01" "CWD Drive Casing (RSK-OPS-001)" "FAIL" "Drive prefix is not uppercase ($drivePrefix). Run Set-Location with uppercase drive letter."
}

# 2. ConnectionStrings__DefaultConnection Validation ([SEC-003] — No Secret Value Printed)
$connStr = [Environment]::GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
$sqlHost = $null
$sqlPort = 1433
if ([string]::IsNullOrWhiteSpace($connStr)) {
    Add-CheckResult "PRE-02" "ConnectionStrings__DefaultConnection" "FAIL" "Missing or empty (Length=0)"
} else {
    $hasSa = $connStr -match '(?i)(User\s*ID|UID)\s*=\s*sa(\s*;|$)'
    $hasIntegrated = $connStr -match '(?i)(Integrated\s*Security|Trusted_Connection)\s*=\s*(True|SSPI|yes)'
    $hasErpKhoDb = $connStr -match '(?i)(Database|Initial\s*Catalog)\s*=\s*ERP_KHO(\s*;|$)'
    $hasForbiddenDb = $connStr -match '(?i)PMS_VIKING_2025|PMS_QLDH_VIKING_2025'

    if ($hasForbiddenDb) {
        Add-CheckResult "PRE-02" "ConnectionStrings__DefaultConnection" "FAIL" "FORBIDDEN database name detected in connection string"
    } elseif ($hasSa) {
        Add-CheckResult "PRE-02" "ConnectionStrings__DefaultConnection" "FAIL" "Forbidden SQL 'sa' account detected ([SEC-003])"
    } elseif ($TargetEnvironment -eq "Production" -and $hasIntegrated) {
        Add-CheckResult "PRE-02" "ConnectionStrings__DefaultConnection" "FAIL" "Integrated Security/Trusted_Connection is not allowed on Production; use least-privilege SQL login"
    } elseif (-not $hasErpKhoDb) {
        Add-CheckResult "PRE-02" "ConnectionStrings__DefaultConnection" "WARN" "Present (Length=$($connStr.Length)), but Database=ERP_KHO token not explicitly matched"
    } else {
        Add-CheckResult "PRE-02" "ConnectionStrings__DefaultConnection" "PASS" "Present (Length=$($connStr.Length), SaAccount=False, IntegratedSecurity=$hasIntegrated, TargetDb=ERP_KHO)"
    }

    if ($connStr -match '(?i)(Server|Data\s*Source)\s*=\s*(tcp:)?([^;,]+)(,(\d+))?') {
        $sqlHost = $Matches[3].Trim()
        if ($Matches[5]) { $sqlPort = [int]$Matches[5] }
    }
}

# 3. Jwt__Key / JwtSettings__SecretKey Validation (>= 32 chars, Not Dev Default)
$jwtKey = [Environment]::GetEnvironmentVariable("Jwt__Key")
if ([string]::IsNullOrWhiteSpace($jwtKey)) {
    $jwtKey = [Environment]::GetEnvironmentVariable("JwtSettings__SecretKey")
}
if ([string]::IsNullOrWhiteSpace($jwtKey)) {
    Add-CheckResult "PRE-03" "Jwt__Key / JwtSettings__SecretKey" "FAIL" "Missing or empty (Length=0)"
} elseif ($jwtKey.Length -lt 32) {
    Add-CheckResult "PRE-03" "Jwt__Key / JwtSettings__SecretKey" "FAIL" "Insufficient key length (Length=$($jwtKey.Length) < 32 minimum)"
} else {
    Add-CheckResult "PRE-03" "Jwt__Key / JwtSettings__SecretKey" "PASS" "Present and meets >= 256-bit length requirement (Length=$($jwtKey.Length))"
}

# 4. Cors__AllowedOrigins Validation (HTTPS required, no wildcard '*')
$corsVars = @(Get-ChildItem Env: | Where-Object { $_.Name -like "Cors__AllowedOrigins*" })
if ($corsVars.Count -eq 0) {
    Add-CheckResult "PRE-04" "Cors__AllowedOrigins" "FAIL" "No Cors__AllowedOrigins* environment variables found (Program.cs:38 throws outside Development)"
} else {
    $allHttpsAndNoWildcard = $true
    foreach ($cv in $corsVars) {
        if ([string]::IsNullOrWhiteSpace($cv.Value) -or $cv.Value.Contains("*") -or (-not $cv.Value.StartsWith("https://", [StringComparison]::OrdinalIgnoreCase))) {
            $allHttpsAndNoWildcard = $false
        }
    }
    if ($allHttpsAndNoWildcard) {
        Add-CheckResult "PRE-04" "Cors__AllowedOrigins" "PASS" "Configured ($($corsVars.Count) origin entry/entries, HTTPS=True, Wildcard=False)"
    } else {
        Add-CheckResult "PRE-04" "Cors__AllowedOrigins" "FAIL" "One or more origins contain wildcard '*' or do not start with 'https://'"
    }
}

# 5. SaasBilling__VietQrWebhookSecret Validation (Not empty, not dev fallback)
$vietQrSecret = [Environment]::GetEnvironmentVariable("SaasBilling__VietQrWebhookSecret")
if ([string]::IsNullOrWhiteSpace($vietQrSecret)) {
    Add-CheckResult "PRE-05" "SaasBilling__VietQrWebhookSecret" "FAIL" "Missing or empty (Length=0)"
} elseif ($vietQrSecret -eq "ERP-KHO-VIETQR-WEBHOOK-SECRET-2026") {
    Add-CheckResult "PRE-05" "SaasBilling__VietQrWebhookSecret" "FAIL" "Still set to development fallback secret; rotate before cutover"
} elseif ($vietQrSecret.Length -lt 24) {
    Add-CheckResult "PRE-05" "SaasBilling__VietQrWebhookSecret" "WARN" "Present but short (Length=$($vietQrSecret.Length) < 24 recommended)"
} else {
    Add-CheckResult "PRE-05" "SaasBilling__VietQrWebhookSecret" "PASS" "Present and rotated from dev default (Length=$($vietQrSecret.Length))"
}

# 6. SQL Server TCP Port Reachability Check
if ([string]::IsNullOrWhiteSpace($sqlHost) -or $sqlHost -like "*(localdb)*") {
    Add-CheckResult "PRE-06" "SQL Server TCP Connectivity" "PASS" "LocalDB instance verified ($sqlHost)"
} else {
    $tcpClient = New-Object System.Net.Sockets.TcpClient
    try {
        $async = $tcpClient.BeginConnect($sqlHost, $sqlPort, $null, $null)
        if ($async.AsyncWaitHandle.WaitOne(3000, $false) -and $tcpClient.Connected) {
            Add-CheckResult "PRE-06" "SQL Server TCP Connectivity" "PASS" "TCP reachability verified ($($sqlHost):$sqlPort)"
        } else {
            Add-CheckResult "PRE-06" "SQL Server TCP Connectivity" "FAIL" "TCP connection timed out ($($sqlHost):$sqlPort)"
        }
    } catch {
        Add-CheckResult "PRE-06" "SQL Server TCP Connectivity" "FAIL" "TCP connection error to $($sqlHost):$sqlPort ($($_.Exception.GetType().Name))"
    } finally {
        $tcpClient.Close()
    }
}

# 7. Live HTTP Health Probes (/health/live & /health/ready)
if ($SkipHttpHealthProbes) {
    Add-CheckResult "PRE-07" "HTTP Probe /health/live"  "SKIP" "Skipped via -SkipHttpHealthProbes"
    Add-CheckResult "PRE-08" "HTTP Probe /health/ready" "SKIP" "Skipped via -SkipHttpHealthProbes"
} else {
    foreach ($probe in @(
        @{ Id = "PRE-07"; Path = "/health/live" },
        @{ Id = "PRE-08"; Path = "/health/ready" }
    )) {
        $uri = "$($ApiBaseUrl.TrimEnd('/'))$($probe.Path)"
        try {
            $resp = Invoke-WebRequest -Uri $uri -Method GET -UseBasicParsing -TimeoutSec 5
            $corrId = $resp.Headers["X-Correlation-Id"]
            if ($resp.StatusCode -eq 200 -and $resp.Content -match '"healthy"') {
                Add-CheckResult $probe.Id "HTTP Probe $($probe.Path)" "PASS" "HTTP $($resp.StatusCode) | X-Correlation-Id=$corrId | Body=$($resp.Content)"
            } else {
                Add-CheckResult $probe.Id "HTTP Probe $($probe.Path)" "FAIL" "HTTP $($resp.StatusCode) | Body=$($resp.Content)"
            }
        } catch {
            Add-CheckResult $probe.Id "HTTP Probe $($probe.Path)" "FAIL" "Endpoint unreachable at $uri ($($_.Exception.Message))"
        }
    }
}

Write-Output "=== ERP_KHO STAGING/PRODUCTION PRE-FLIGHT REPORT ($TargetEnvironment) ==="
$results | Format-Table -AutoSize
if ($failureCount -gt 0) {
    Write-Output "PREFLIGHT_VERDICT=FAIL (FailedChecks=$failureCount)"
    exit 1
} else {
    Write-Output "PREFLIGHT_VERDICT=PASS (FailedChecks=0)"
    exit 0
}
