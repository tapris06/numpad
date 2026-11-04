param([switch]$SkipTests)
. (Join-Path $PSScriptRoot 'tools\env.ps1')
$templateArchive = Join-Path $PSScriptRoot '.tools\export-templates.tpz'
$templateRoot = Join-Path $PSScriptRoot '.tools\export-templates'
if (-not (Test-Path -LiteralPath (Join-Path $templateRoot 'windows_release_x86_64.exe'))) {
    if (-not (Test-Path -LiteralPath $templateArchive)) {
        & curl.exe --location --fail --retry 3 --output "$templateArchive.part" 'https://github.com/godotengine/godot/releases/download/4.7.2-stable/Godot_v4.7.2-stable_mono_export_templates.tpz'
        if ($LASTEXITCODE -ne 0) { throw 'Export template download failed.' }
        Move-Item -LiteralPath "$templateArchive.part" -Destination $templateArchive
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    New-Item -ItemType Directory -Force -Path $templateRoot | Out-Null
    $archive = [IO.Compression.ZipFile]::OpenRead($templateArchive)
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.Name -in @('windows_debug_x86_64.exe', 'windows_release_x86_64.exe')) {
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $templateRoot $entry.Name), $true)
            }
        }
    } finally { $archive.Dispose() }
}
if (-not $SkipTests) { & (Join-Path $PSScriptRoot 'test.ps1') }
& (Join-Path $PSScriptRoot 'build.ps1')
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'build\windows') | Out-Null
& $godotExecutable --headless --path $PSScriptRoot --editor --import --log-file (Join-Path $PSScriptRoot '.tools\export-import.log')
if ($LASTEXITCODE -ne 0) { throw 'Resource import failed.' }
& $godotExecutable --headless --path $PSScriptRoot --export-release 'Windows Desktop' --log-file (Join-Path $PSScriptRoot '.tools\export.log')
if ($LASTEXITCODE -ne 0) { throw 'Windows export failed.' }
$exportErrors = Select-String -LiteralPath (Join-Path $PSScriptRoot '.tools\export.log') -Pattern '^ERROR:'
if ($exportErrors) { throw 'Godot reported export errors. See .tools/export.log.' }
$managedOutput = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'build\windows') -Recurse -Filter 'NumpadNine.dll' -File)
if ($managedOutput.Count -eq 0) { throw 'The export does not contain the game assembly.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\play-guide.md') -Destination (Join-Path $PSScriptRoot 'build\windows\플레이 안내.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $PSScriptRoot 'build\windows\THIRD_PARTY_NOTICES.md')
& (Join-Path $PSScriptRoot 'tools\package.ps1')
