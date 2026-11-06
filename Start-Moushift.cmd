@echo off
if not exist "%~dp0artifacts\Moushift-0.14.4\Moushift.exe" (
  echo Build the project first: scripts\Build.ps1
  pause
  exit /b 1
)
start "Moushift" "%~dp0artifacts\Moushift-0.14.4\Moushift.exe"