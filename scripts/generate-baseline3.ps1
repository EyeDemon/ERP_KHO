function Get-FileSha256($filePath) {
    $stream = [IO.File]::OpenRead($filePath)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    $hashBytes = $sha256.ComputeHash($stream)
    $stream.Close()
    $sha256.Dispose()
    return [System.BitConverter]::ToString($hashBytes).Replace("-", "").ToLowerInvariant()
}

$output = (powershell -NoProfile -ExecutionPolicy Bypass -File d:\ERP_KHO\scripts\check-encoding.ps1 2>&1) | Out-String
$lines = $output -split "`r`n"
$baseline = @()

$oldBaselinePath = "d:\ERP_KHO\.agent\encoding-baseline.json"
if (Test-Path $oldBaselinePath) {
    $baseline = Get-Content $oldBaselinePath -Raw | ConvertFrom-Json
    if ($null -eq $baseline) { $baseline = @() }
}

$added = 0
foreach ($line in $lines) {
    if ($line -match "^(.*?):\s+(.*?)\s+\(Contains.*?(U\+[0-9A-F]{4})?\)") {
        $defect = $matches[1]
        $path = $matches[2]
        if ($defect -eq "CONTROL_CHAR" -and $matches[3]) {
            $defect = "CONTROL_CHAR_" + $matches[3]
        }
        
        if ($path -match "KAN-2817") { continue }
        
        if (Test-Path -LiteralPath $path) {
            $relPath = (Resolve-Path -Relative -LiteralPath $path)
            if ($relPath.StartsWith(".\") -or $relPath.StartsWith("./")) { $relPath = $relPath.Substring(2) }
            $relPath = $relPath -replace '\\', '/'
            $hash = Get-FileSha256 $path
            
            $found = $false
            foreach ($b in $baseline) {
                if ($b.path -eq $relPath -and $b.defect -eq $defect -and $b.hash -eq $hash) { $found = $true; break }
            }
            if (-not $found) {
                $baseline += @{ path = $relPath; defect = $defect; hash = $hash }
                $added++
            }
        }
    }
}

if ($added -gt 0) {
    $baseline | ConvertTo-Json -Depth 10 | Set-Content -Path $oldBaselinePath -Encoding UTF8
}
