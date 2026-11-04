@echo off
if not exist "%~dp0build\windows\NumpadNine.exe" (
  echo Build not found. Run export.ps1 first.
  pause
  exit /b 1
)
start "" "%~dp0build\windows\NumpadNine.exe"
