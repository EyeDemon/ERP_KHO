param(
    [string[]]$Roots = @()
)

$ErrorActionPreference = "Stop"

# 1. Resolve Repo Root using $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path

$extensions = @(".cs", ".csproj", ".json", ".md", ".ts", ".tsx", ".js", ".jsx", ".css", ".html", ".yml", ".yaml", ".ps1")
$excludedSegments = @(".git", ".vs", "bin", "obj", "node_modules", "dist")

$baseRoots = @(
    "ERP.Api", "ERP.Application", "ERP.Domain", "ERP.Infrastructure",
    "ERP.Api.Tests", "ERP.Application.Tests",
    "frontend", "docs", "scripts",
    ".agent/rules",
    ".agent/orders", ".agent/reports", ".agent/reviews"
)

if ($Roots.Length -gt 0) {
    $resolvedRoots = @()
    foreach ($r in $Roots) {
        $resolvedRoots += (Resolve-Path -LiteralPath $r).Path
    }
    $baseRoots = $resolvedRoots
    $gitFiles = @()
} else {
    $resolvedRoots = @()
    foreach ($r in $baseRoots) {
        $p = Join-Path $repoRoot $r
        if (Test-Path -LiteralPath $p) {
            $resolvedRoots += (Resolve-Path -LiteralPath $p).Path
        }
    }
    $baseRoots = $resolvedRoots

    $gitFiles = @()
    try {
        $oldCwd = (Get-Location).Path
        Set-Location $repoRoot
        $gitOut = (git ls-files --others --modified --exclude-standard 2>$null)
        if ($LASTEXITCODE -eq 0 -and $null -ne $gitOut) {
            $gitFiles = $gitOut
        }
        Set-Location $oldCwd
    } catch {}
}

$allFilesToScan = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

foreach ($root in $baseRoots) {
    if (Test-Path -LiteralPath $root) {
        Get-ChildItem -LiteralPath $root -Recurse -File | ForEach-Object {
            $allFilesToScan.Add($_.FullName) | Out-Null
        }
    }
}

foreach ($gf in $gitFiles) {
    $absPath = Join-Path $repoRoot $gf
    if (Test-Path -LiteralPath $absPath -PathType Leaf) {
        $full = (Resolve-Path -LiteralPath $absPath).Path
        $allFilesToScan.Add($full) | Out-Null
    }
}

# 2 & 3. Fail-closed Baseline Loading & Validation
$baselinePath = Join-Path $repoRoot ".agent/encoding-baseline.json"
$baseline = @{}

if (-not (Test-Path -LiteralPath $baselinePath -PathType Leaf)) {
    [Console]::Error.WriteLine("FATAL: Baseline file missing at $baselinePath")
    exit 1
}

$blContent = $null
$strictUtf8Baseline = [System.Text.UTF8Encoding]::new($false, $true)
try {
    # Validate strict UTF-8 at byte level (will throw on invalid sequences)
    $rawBytes = [IO.File]::ReadAllBytes($baselinePath)
    [void]$strictUtf8Baseline.GetString($rawBytes)
    # Parse JSON using Get-Content -Raw to avoid PS 5.1 pipeline serializer bug
    $blContent = Get-Content -Raw -LiteralPath $baselinePath -Encoding UTF8 | ConvertFrom-Json
} catch {
    [Console]::Error.WriteLine("FATAL: Baseline file contains invalid UTF-8 byte sequences or malformed JSON")
    exit 1
}

if ($null -ne $blContent) {
    foreach ($entry in $blContent) {
        if ([string]::IsNullOrWhiteSpace($entry.path)) {
            [Console]::Error.WriteLine("FATAL: Baseline entry missing 'path'")
            exit 1
        }
        if ([string]::IsNullOrWhiteSpace($entry.defect)) {
            [Console]::Error.WriteLine("FATAL: Baseline entry missing 'defect'")
            exit 1
        }
        if ([string]::IsNullOrWhiteSpace($entry.hash)) {
            [Console]::Error.WriteLine("FATAL: Baseline entry missing 'hash'")
            exit 1
        }

        # Path validation
        $p = $entry.path -replace '\\', '/'
        if ($p -match "\.\./") {
            [Console]::Error.WriteLine("FATAL: Baseline path escapes workspace (contains '../'): $p")
            exit 1
        }
        if ($p.StartsWith("/") -or $p -match "^[A-Za-z]:") {
            [Console]::Error.WriteLine("FATAL: Baseline path must be relative to repository root: $p")
            exit 1
        }

        # Defect validation
        $allowedDefects = @("INVALID_UTF8", "U\+FFFD", "MOJIBAKE", "CONTROL_CHAR_U\+[0-9A-F]{4}")
        $validDefect = $false
        foreach ($d in $allowedDefects) {
            if ($entry.defect -match "^$d$") { $validDefect = $true; break }
        }
        if (-not $validDefect) {
            [Console]::Error.WriteLine("FATAL: Baseline defect invalid: $($entry.defect)")
            exit 1
        }
        if ($entry.defect -match "^CONTROL_CHAR_U\+([0-9A-Fa-f]{4})$") {
            $hexValue = [Convert]::ToInt32($matches[1], 16)
            $allowedControls = @(0x0009, 0x000A, 0x000D)
            if ($hexValue -gt 0x001F -or $allowedControls -contains $hexValue) {
                [Console]::Error.WriteLine("FATAL: Baseline defect specifies non-C0 or explicitly allowed control char: $($entry.defect)")
                exit 1
            }
        }

        # Hash validation
        if (-not ($entry.hash -cmatch "^[0-9a-f]{64}$")) {
            [Console]::Error.WriteLine("FATAL: Baseline hash must be exactly 64 lowercase hex characters: $($entry.hash)")
            exit 1
        }

        $key = $p + "|" + $entry.defect + "|" + $entry.hash
        if ($baseline.ContainsKey($key)) {
            [Console]::Error.WriteLine("FATAL: Duplicate identical baseline key found: $key")
            exit 1
        }
        $baseline[$key] = $true
    }
}

function Get-FileSha256($filePath) {
    $stream = [IO.File]::OpenRead($filePath)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    $hashBytes = $sha256.ComputeHash($stream)
    $stream.Close()
    $sha256.Dispose()
    return [System.BitConverter]::ToString($hashBytes).Replace("-", "").ToLowerInvariant()
}

$strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
$failures = [System.Collections.Generic.List[string]]::new()

$sequenceMarkers = @(
    ("Ng" + [char]0x00C6 + [char]0x00B0),
    ([char]0x00C3 + [char]0x00A1),
    ([char]0x00C3 + [char]0x00A2),
    ([char]0x00C3 + [char]0x00B3),
    ([char]0x00C3 + [char]0x00B5),
    ([char]0x00C3 + [char]0x00BA),
    ([char]0x00C3 + [char]0x00BD),
    ([char]0x00C3 + [char]0x00AA),
    ([char]0x00C4 + [char]0x2018),
    ([char]0x00C4 + " "),
    ([char]0x00E1 + [char]0x00BA),
    ([char]0x00E1 + [char]0x00BB)
)

$allowedControls = @([char]0x0009, [char]0x000A, [char]0x000D)

foreach ($path in $allFilesToScan) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
    $file = $null
    try {
        $file = Get-Item -LiteralPath $path -Force -ErrorAction Stop
    } catch {
        continue
    }
    if ($null -eq $file) { continue }
    $pathSegments = $file.FullName.Split([IO.Path]::DirectorySeparatorChar)
    if ($extensions -notcontains $file.Extension.ToLowerInvariant()) { continue }
    
    $excluded = $false
    foreach ($seg in $excludedSegments) {
        if ($pathSegments -contains $seg) { $excluded = $true; break }
    }
    if ($excluded) { continue }
    
    # Check baseline suppression
    $relPath = $null
    if ($path.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
        $relPath = $path.Substring($repoRoot.Length)
        if ($relPath.StartsWith("\") -or $relPath.StartsWith("/")) {
            $relPath = $relPath.Substring(1)
        }
        $relPath = $relPath -replace '\\', '/'
    } else {
        $relPath = $path -replace '\\', '/'
    }
    
    $fileHash = $null

    try {
        $text = $strictUtf8.GetString([IO.File]::ReadAllBytes($path))
    }
    catch {
        $defect = "INVALID_UTF8"
        if ($null -eq $fileHash) { $fileHash = Get-FileSha256 $path }
        $key = $relPath + "|" + $defect + "|" + $fileHash
        if (-not $baseline.ContainsKey($key)) {
            $failures.Add("INVALID_UTF8: $path (Byte sequence is not valid UTF-8)")
        }
        continue
    }

    if ($text.Contains([char]0xFFFD)) {
        $defect = "U+FFFD"
        if ($null -eq $fileHash) { $fileHash = Get-FileSha256 $path }
        $key = $relPath + "|" + $defect + "|" + $fileHash
        if (-not $baseline.ContainsKey($key)) {
            $failures.Add("U+FFFD: $path (Contains the Unicode replacement character U+FFFD)")
        }
        continue
    }
    
    $hasControl = $false
    for ($i = 0; $i -le 0x001F; $i++) {
        $c = [char]$i
        if ($allowedControls -contains $c) { continue }
        if ($text.Contains($c)) {
            $code = "U+{0:X4}" -f $i
            $defect = "CONTROL_CHAR_$code"
            if ($null -eq $fileHash) { $fileHash = Get-FileSha256 $path }
            $key = $relPath + "|" + $defect + "|" + $fileHash
            if (-not $baseline.ContainsKey($key)) {
                $failures.Add("CONTROL_CHAR: $path (Contains unexpected C0 control character $code)")
                $hasControl = $true
                break
            }
        }
    }
    if ($hasControl) { continue }

    foreach ($marker in $sequenceMarkers) {
        if ($text.Contains($marker)) {
            $defect = "MOJIBAKE"
            if ($null -eq $fileHash) { $fileHash = Get-FileSha256 $path }
            $key = $relPath + "|" + $defect + "|" + $fileHash
            if (-not $baseline.ContainsKey($key)) {
                $failures.Add("MOJIBAKE: $path (Contains known mojibake sequence)")
                break
            }
        }
    }
}

if ($failures.Count -gt 0) {
    $failures | Sort-Object -Unique | ForEach-Object { [Console]::Error.WriteLine($_) }
    exit 1
}

Write-Output "Encoding check passed: all scanned files are valid UTF-8 and no mojibake markers were found."
exit 0
