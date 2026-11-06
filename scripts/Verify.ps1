param([switch]$StartupSmoke, [switch]$InteractiveUi)
$ErrorActionPreference = 'Stop'
$clickloomRoot = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $clickloomRoot '.local\dotnet'
$env:DOTNET_NOLOGO = '1'
$previousClickloomData = $env:CLICKLOOM_DATA_DIR
Push-Location $clickloomRoot
try {
    & (Join-Path $PSScriptRoot 'Build.ps1') -NoPublish
    dotnet run --project tests\Clickloom.Core.Tests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    dotnet run --project tests\Clickloom.Windows.Tests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Windows tests failed.' }
    $env:CLICKLOOM_DATA_DIR = Join-Path $clickloomRoot '.local\verify-state'
    foreach ($circleTheme in @('light', 'dark')) {
        dotnet src\Clickloom.App\bin\Release\net10.0-windows\Clickloom.dll --render-circle (Join-Path $clickloomRoot ('docs\ui-010\' + $circleTheme + '-circle.png')) --verify-circle ('--theme=' + $circleTheme)
        if ($LASTEXITCODE -ne 0) { throw 'Offscreen circle verification failed.' }
        dotnet src\Clickloom.App\bin\Release\net10.0-windows\Clickloom.dll --render-circle-editor (Join-Path $clickloomRoot ('docs\ui-011\editor-' + $circleTheme + '.png')) --verify-editor --folder ('--theme=' + $circleTheme)
        if ($LASTEXITCODE -ne 0) { throw 'Circle workspace verification failed.' }
        dotnet src\Clickloom.App\bin\Release\net10.0-windows\Clickloom.dll --render-circle-editor (Join-Path $clickloomRoot ('docs\ui-0112\drag-' + $circleTheme + '.png')) --verify-editor --folder --drag-preview ('--theme=' + $circleTheme)
        if ($LASTEXITCODE -ne 0) { throw 'Drag card verification failed.' }
        dotnet src\Clickloom.App\bin\Release\net10.0-windows\Clickloom.dll --render-circle (Join-Path $clickloomRoot ('docs\ui-011\folder-' + $circleTheme + '.png')) --folder ('--theme=' + $circleTheme)
        if ($LASTEXITCODE -ne 0) { throw 'Circle folder arc capture failed.' }
        dotnet src\Clickloom.App\bin\Release\net10.0-windows\Clickloom.dll --render-hid-learning (Join-Path $clickloomRoot ('docs\ui-012\learning-' + $circleTheme + '.png')) --verify-hid ('--theme=' + $circleTheme)
        if ($LASTEXITCODE -ne 0) { throw 'HID learning preview failed.' }
    }
    dotnet src\Clickloom.App\bin\Release\net10.0-windows\Clickloom.dll --render-circle-editor (Join-Path $clickloomRoot 'docs\ui-0111\empty-folder.png') --empty-folder --theme=light
    if ($LASTEXITCODE -ne 0) { throw 'Empty folder shortcut entry capture failed.' }
    dotnet src\Clickloom.App\bin\Release\net10.0-windows\Clickloom.dll --render-reset-dialog (Join-Path $clickloomRoot 'docs\ui-010\reset-buttons.png') --theme=light
    if ($LASTEXITCODE -ne 0) { throw 'Offscreen reset dialog verification failed.' }
    dotnet src\Clickloom.App\bin\Release\net10.0-windows\Clickloom.dll --render-workspace (Join-Path $clickloomRoot 'docs\ui-010\buttons.png') --verify-device-ui --theme=light
    if ($LASTEXITCODE -ne 0) { throw 'Device profile editor verification failed.' }
    dotnet src\Clickloom.App\bin\Release\net10.0-windows\Clickloom.dll --render-devices (Join-Path $clickloomRoot 'docs\ui-010\devices.png') --theme=light
    if ($LASTEXITCODE -ne 0) { throw 'Devices capture failed.' }
    dotnet src\Clickloom.App\bin\Release\net10.0-windows\Clickloom.dll --render-setup (Join-Path $clickloomRoot 'docs\ui-010\setup.png') --theme=dark
    if ($LASTEXITCODE -ne 0) { throw 'Device setup capture failed.' }
    foreach ($designTheme in @('light', 'dark')) {
        dotnet src\Clickloom.App\bin\Release\net10.0-windows\Clickloom.dll --render-devices (Join-Path $clickloomRoot ('docs\ui-014\' + $designTheme + '-gallery.png')) --design-fixture --verify-design ('--theme=' + $designTheme)
        if ($LASTEXITCODE -ne 0) { throw 'Gallery/inspector, theme or profile preservation checks failed.' }
    }
    dotnet src\Clickloom.App\bin\Release\net10.0-windows\Clickloom.dll --render-devices (Join-Path $clickloomRoot 'docs\ui-014\light-compact.png') --design-fixture --compact --theme=light
    if ($LASTEXITCODE -ne 0) { throw 'Compact gallery capture failed.' }
    if ($InteractiveUi) {
        dotnet src\Clickloom.App\bin\Release\net10.0-windows\Clickloom.dll --render-preview (Join-Path $clickloomRoot 'docs\clickloom-preview.png') --verify-ui --theme=light
        if ($LASTEXITCODE -ne 0) { throw 'WPF editor verification failed.' }
    }
    if ($StartupSmoke) {
        dotnet src\Clickloom.App\bin\Release\net10.0-windows\Clickloom.dll --smoke-test --minimized
        if ($LASTEXITCODE -ne 0) { throw 'Windows startup smoke failed.' }
    }
} finally { $env:CLICKLOOM_DATA_DIR = $previousClickloomData; Pop-Location }
