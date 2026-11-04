$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageProjectRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = (Resolve-Path -LiteralPath (Join-Path $packageProjectRoot 'build\windows')).Path
$releasePrefix = $releaseRoot.TrimEnd('\') + '\'
$packagePath = Join-Path $packageProjectRoot 'build\NumpadNine-Windows-x64.zip'
$packageStream = [IO.File]::Open($packagePath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $packageArchive = [IO.Compression.ZipArchive]::new($packageStream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        # Windows can retain replacement backups while an older game instance is open.
        $releaseFiles = Get-ChildItem -LiteralPath $releaseRoot -Recurse -File |
            Where-Object Name -NotMatch '~RF[0-9a-f]+\.TMP$'
        foreach ($releaseFile in $releaseFiles) {
            if (-not $releaseFile.FullName.StartsWith($releasePrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Package source file is outside the release directory.'
            }
            $entryName = $releaseFile.FullName.Substring($releasePrefix.Length).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $packageArchive, $releaseFile.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null
        }
    } finally { $packageArchive.Dispose() }
} finally { $packageStream.Dispose() }
Write-Output $packagePath
