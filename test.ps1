. (Join-Path $PSScriptRoot 'tools\env.ps1')
Push-Location $PSScriptRoot
try {
    & (Join-Path $env:DOTNET_ROOT 'dotnet.exe') run --project 'tests/NumpadNine.Tests' --configuration Release -- $PSScriptRoot
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
} finally { Pop-Location }
