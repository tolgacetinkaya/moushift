param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$clickloomRoot = Split-Path $PSScriptRoot -Parent
$clickloomPriorData = $env:CLICKLOOM_DATA_DIR
Push-Location $clickloomRoot
try {
    if (-not $NoBuild) { & (Join-Path $PSScriptRoot 'Build.ps1') -NoPublish }
    $env:CLICKLOOM_DATA_DIR = Join-Path $clickloomRoot '.local\theme-render-state-014'
    $clickloomDll = Join-Path $clickloomRoot 'src\Clickloom.App\bin\Release\net10.0-windows\Moushift.dll'
    foreach ($clickloomTheme in @('light', 'dark')) {
        foreach ($clickloomPage in @('workspace', 'devices', 'settings', 'setup', 'circle', 'reset-dialog')) {
            $clickloomOutput = Join-Path $clickloomRoot ('docs\ui-010\' + $clickloomTheme + '-' + $clickloomPage + '.png')
            dotnet $clickloomDll ('--render-' + $clickloomPage) $clickloomOutput ('--theme=' + $clickloomTheme)
            if ($LASTEXITCODE -ne 0) { throw ('Offscreen capture failed: ' + $clickloomTheme + '/' + $clickloomPage) }
        }
        dotnet $clickloomDll --render-workspace (Join-Path $clickloomRoot ('docs\ui-010\' + $clickloomTheme + '-compact.png')) ('--theme=' + $clickloomTheme) --compact
        if ($LASTEXITCODE -ne 0) { throw 'Compact offscreen capture failed.' }
    }
} finally { $env:CLICKLOOM_DATA_DIR = $clickloomPriorData; Pop-Location }
