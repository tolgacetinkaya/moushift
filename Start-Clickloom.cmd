@echo off
if not exist "%~dp0artifacts\Clickloom-0.12.0\Clickloom.exe" (
  echo Build the project first: scripts\Build.ps1
  pause
  exit /b 1
)
start "Clickloom" "%~dp0artifacts\Clickloom-0.12.0\Clickloom.exe"