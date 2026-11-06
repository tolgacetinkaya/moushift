param([switch]$NoPublish)
$ErrorActionPreference = 'Stop'
$clickloomRoot = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $clickloomRoot '.local\dotnet'
$env:DOTNET_NOLOGO = '1'
Push-Location $clickloomRoot
try {
    dotnet build Clickloom.slnx -c Release --disable-build-servers -m:1 -p:BuildInParallel=false -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }
    if (-not $NoPublish) {
        $clickloomProject = [xml](Get-Content src\Clickloom.App\Clickloom.App.csproj -Raw)
        $clickloomVersion = [string]$clickloomProject.Project.PropertyGroup.Version
        if ($clickloomVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Project version must be a numeric semantic version.' }
        $clickloomFolder = 'Moushift-' + $clickloomVersion
        $clickloomOutput = Join-Path 'artifacts' $clickloomFolder
        dotnet publish src\Clickloom.App\Clickloom.App.csproj -c Release --no-restore --self-contained false -o $clickloomOutput --disable-build-servers -m:1 -p:BuildInParallel=false -v minimal
        if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)." }
        $clickloomLauncher = @'
@echo off
if not exist "%~dp0artifacts\__FOLDER__\Moushift.exe" (
  echo Build the project first: scripts\Build.ps1
  pause
  exit /b 1
)
start "Moushift" "%~dp0artifacts\__FOLDER__\Moushift.exe"
'@
        [System.IO.File]::WriteAllText((Join-Path $clickloomRoot 'Start-Moushift.cmd'), $clickloomLauncher.Replace('__FOLDER__', $clickloomFolder))
        [System.IO.File]::WriteAllText((Join-Path $clickloomRoot 'Start-Clickloom.cmd'), '@echo off' + [Environment]::NewLine + 'call "%~dp0Start-Moushift.cmd"' + [Environment]::NewLine)
    }
} finally { Pop-Location }
