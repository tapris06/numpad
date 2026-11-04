$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$toolRoot = Join-Path $projectRoot '.tools'
New-Item -ItemType Directory -Force -Path $toolRoot | Out-Null
New-Item -ItemType File -Force -Path (Join-Path $toolRoot '.gdignore') | Out-Null

function Get-ProjectArchive([string]$Uri, [string]$Target) {
    if (-not (Test-Path -LiteralPath $Target)) {
        & curl.exe --location --fail --retry 3 --output "$Target.part" $Uri
        if ($LASTEXITCODE -ne 0) { throw "Download failed: $Uri" }
        Move-Item -LiteralPath "$Target.part" -Destination $Target
    }
}

$sdkExecutable = Join-Path $toolRoot 'dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdkExecutable)) {
    $archive = Join-Path $toolRoot 'dotnet-sdk.zip'
    Get-ProjectArchive 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.403/dotnet-sdk-10.0.403-win-x64.zip' $archive
    Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $toolRoot 'dotnet') -Force
}
$editorRoot = Join-Path $toolRoot 'godot\Godot_v4.7.2-stable_mono_win64'
$editorExecutable = Join-Path $editorRoot 'Godot_v4.7.2-stable_mono_win64_console.exe'
if (-not (Test-Path -LiteralPath $editorExecutable)) {
    $archive = Join-Path $toolRoot 'godot.zip'
    Get-ProjectArchive 'https://github.com/godotengine/godot/releases/download/4.7.2-stable/Godot_v4.7.2-stable_mono_win64.zip' $archive
    Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $toolRoot 'godot') -Force
}
New-Item -ItemType File -Force -Path (Join-Path $editorRoot '._sc_') | Out-Null
. (Join-Path $projectRoot 'tools\env.ps1')
& $sdkExecutable --version
& $editorExecutable --version
Write-Output '프로젝트 개발 도구 준비 완료. .\test.ps1 또는 .\run.ps1을 실행하세요.'
