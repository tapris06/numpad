. (Join-Path $PSScriptRoot 'tools\env.ps1')
Push-Location $PSScriptRoot
try {
    & (Join-Path $env:DOTNET_ROOT 'dotnet.exe') build 'NumpadNine.csproj' --configuration Debug
    if ($LASTEXITCODE -ne 0) { throw 'Game build failed.' }
} finally { Pop-Location }
