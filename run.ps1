param([switch]$AutoPlay, [switch]$Normal, [switch]$TestExit, [string]$CaptureDir = '', [string]$Chart = '')
. (Join-Path $PSScriptRoot 'tools\env.ps1')
& (Join-Path $PSScriptRoot 'build.ps1')
$gameArguments = @('--path', $PSScriptRoot, '--')
if ($AutoPlay) { $gameArguments += '--autoplay' }
if ($Normal) { $gameArguments += '--normal' }
if ($TestExit) { $gameArguments += '--test-exit' }
if ($CaptureDir) { $gameArguments += "--capture-dir=$CaptureDir" }
if ($Chart) { $gameArguments += "--chart=$Chart" }
& $godotExecutable @gameArguments
exit $LASTEXITCODE
