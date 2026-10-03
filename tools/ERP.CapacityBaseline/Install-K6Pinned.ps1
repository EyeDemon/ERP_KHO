[CmdletBinding(DefaultParameterSetName = 'Download')]
param(
    [Parameter(Mandatory = $true)][string]$DestinationDirectory,
    [Parameter(Mandatory = $true, ParameterSetName = 'Download')][switch]$Download,
    [Parameter(Mandatory = $true, ParameterSetName = 'Fixture')][switch]$VerifyFixture,
    [Parameter(Mandatory = $true, ParameterSetName = 'Fixture')][string]$FixtureArchive,
    [Parameter(Mandatory = $true, ParameterSetName = 'Fixture')][string]$FixtureExpectedSha256
)

$ErrorActionPreference = 'Stop'
$version = 'v2.1.0'
$assetName = 'k6-v2.1.0-windows-amd64.zip'
$officialUri = [Uri]"https://github.com/grafana/k6/releases/download/$version/$assetName"
$expectedSha256 = '185CA503EAD8F0348DAA79C002469E5EB324473C39452F29B5F70B1C1B4C8503'

function Assert-SafeArchive([string]$ArchivePath, [string]$ExpectedHash, [string]$ExtractionRoot) {
    $actual = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash
    if ($actual -ne $ExpectedHash) { throw 'Pinned k6 archive checksum mismatch; the archive is not usable.' }
    Add-Type -AssemblyName System.IO.Compression
    $root = [IO.Path]::GetFullPath($ExtractionRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $stream = [IO.File]::OpenRead($ArchivePath)
    try {
        $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read)
        try {
            foreach ($entry in $zip.Entries) {
                if ([IO.Path]::IsPathRooted($entry.FullName)) { throw 'Archive contains an absolute path.' }
                $target = [IO.Path]::GetFullPath((Join-Path $ExtractionRoot $entry.FullName))
                if (-not $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive entry escapes the destination directory.' }
            }
        } finally { $zip.Dispose() }
    } finally { $stream.Dispose() }
}

if (-not [IO.Path]::IsPathRooted($DestinationDirectory)) { throw 'k6 destination must be an absolute path.' }
if ($VerifyFixture) {
    Assert-SafeArchive (Resolve-Path -LiteralPath $FixtureArchive).Path $FixtureExpectedSha256 $DestinationDirectory
    Write-Output 'Fixture archive checksum and paths verified; no extraction or execution performed.'
    exit 0
}

New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
$archive = Join-Path $DestinationDirectory $assetName
$partial = "$archive.partial"
$extractPath = Join-Path $DestinationDirectory 'k6-v2.1.0-windows-amd64'
$staging = Join-Path $DestinationDirectory ('.k6-v2.1.0-staging-' + [Guid]::NewGuid().ToString('N'))
foreach ($path in @($archive,$partial,$extractPath)) { if (Test-Path -LiteralPath $path) { throw 'Pinned k6 target already exists; overwrite is forbidden.' } }

try {
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $true
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromMinutes(2)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd('ERP-KHO-capacity-tooling')
    try {
        $response = $client.GetAsync($officialUri).GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) { throw "Pinned k6 download failed with HTTP $([int]$response.StatusCode)." }
        $finalUri = $response.RequestMessage.RequestUri
        if ($finalUri.Scheme -ne 'https' -or $finalUri.Host -notin @('github.com','release-assets.githubusercontent.com')) { throw 'Pinned k6 download redirected outside the release allowlist.' }
        $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        [IO.File]::WriteAllBytes($partial, $bytes)
    } finally { $client.Dispose(); $handler.Dispose() }

    Assert-SafeArchive $partial $expectedSha256 $staging
    Move-Item -LiteralPath $partial -Destination $archive
    New-Item -ItemType Directory -Path $staging | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $staging
    $stagedBinary = Get-ChildItem -LiteralPath $staging -Filter k6.exe -File -Recurse | Select-Object -First 1
    if (-not $stagedBinary) { throw 'Pinned k6 binary is missing after extraction.' }
    $versionOutput = & $stagedBinary.FullName version 2>&1
    if ($LASTEXITCODE -ne 0 -or ($versionOutput -join ' ') -notmatch '(^|\s)k6 v2\.1\.0(\s|$)') { throw 'Pinned k6 binary version check failed.' }
    Move-Item -LiteralPath $stagedBinary.Directory.FullName -Destination $extractPath
    Write-Output "Pinned k6 $version verified. Binary: $(Join-Path $extractPath 'k6.exe')"
} catch {
    foreach ($path in @($partial,$staging)) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force } }
    throw
}
