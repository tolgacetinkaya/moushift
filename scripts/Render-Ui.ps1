param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$clickloomRoot = Split-Path $PSScriptRoot -Parent
$priorClickloomData = $env:CLICKLOOM_DATA_DIR
Push-Location $clickloomRoot
try {
    if (-not $NoBuild) { & (Join-Path $PSScriptRoot 'Build.ps1') -NoPublish }
    $env:CLICKLOOM_DATA_DIR = Join-Path $clickloomRoot '.local\ui-review-state'
    $clickloomDll = Join-Path $clickloomRoot 'src\Clickloom.App\bin\Release\net10.0-windows\Moushift.dll'
    $clickloomOutput = Join-Path $clickloomRoot 'docs\ui-05'
    New-Item -ItemType Directory -Path $clickloomOutput -Force | Out-Null
    dotnet $clickloomDll --render-preview (Join-Path $clickloomRoot 'docs\clickloom-preview.png') --verify-ui
    if ($LASTEXITCODE -ne 0) { throw 'WPF controller verification failed.' }
    foreach ($clickloomPage in @('devices', 'devices-sample', 'profiles', 'about', 'about-details', 'ring-editor', 'ring-adjustment', 'ring-folder', 'profile-name', 'ring', 'shortcut-manual', 'action-text', 'action-mouse', 'action-application', 'action-ring')) {
        dotnet $clickloomDll --render-preview (Join-Path $clickloomOutput ($clickloomPage + '.png')) ('--page=' + $clickloomPage)
        if ($LASTEXITCODE -ne 0) { throw ('Screen render failed: ' + $clickloomPage) }
    }
    dotnet $clickloomDll --render-preview (Join-Path $clickloomOutput 'mapping-compact.png') --compact --verify-ui
    if ($LASTEXITCODE -ne 0) { throw 'Compact editor verification failed.' }
    dotnet $clickloomDll --render-preview (Join-Path $clickloomOutput 'mapping-wide.png') --wide --page=mapping-expanded --verify-ui
    if ($LASTEXITCODE -ne 0) { throw 'Wide editor verification failed.' }
    dotnet $clickloomDll --render-preview (Join-Path $clickloomOutput 'mapping-compact-expanded.png') --compact --page=mapping-expanded
    if ($LASTEXITCODE -ne 0) { throw 'Expanded compact menu verification failed.' }
} finally { $env:CLICKLOOM_DATA_DIR = $priorClickloomData; Pop-Location }
