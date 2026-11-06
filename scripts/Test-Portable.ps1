param([Parameter(Mandatory)][string]$AppDirectory)
$ErrorActionPreference = 'Stop'
$appRoot = (Resolve-Path -LiteralPath $AppDirectory).Path
$repository = Split-Path $PSScriptRoot -Parent
$checkRoot = Join-Path $repository ('.local/portable-check/' + [guid]::NewGuid().ToString('N'))
$emptyRuntime = Join-Path $checkRoot 'no-shared-runtime'
New-Item -ItemType Directory -Path $emptyRuntime -Force | Out-Null
$names = @('DOTNET_ROOT','DOTNET_ROOT_X64','DOTNET_MULTILEVEL_LOOKUP','CLICKLOOM_DATA_DIR','DOTNET_HOST_TRACE','DOTNET_HOST_TRACEFILE')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $env:DOTNET_ROOT = $emptyRuntime; $env:DOTNET_ROOT_X64 = $emptyRuntime
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    $env:CLICKLOOM_DATA_DIR = Join-Path $checkRoot 'profiles'
    $env:DOTNET_HOST_TRACE = '1'
    foreach ($theme in @('light','dark')) {
        $png = Join-Path $checkRoot ($theme + '.png')
        $env:DOTNET_HOST_TRACEFILE = Join-Path $checkRoot ($theme + '-host.txt')
        $arguments = '--render-devices "{0}" --design-fixture --verify-design --theme={1}' -f $png,$theme
        $process = Start-Process -FilePath (Join-Path $appRoot 'Moushift.exe') -ArgumentList $arguments -WorkingDirectory $appRoot -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $png)) { throw "Standalone WPF launch failed: $theme ($($process.ExitCode))." }
        $trace = Get-Content -LiteralPath $env:DOTNET_HOST_TRACEFILE -Raw
        if ($trace -notmatch ('(?im)CoreCLR path.*' + [regex]::Escape((Join-Path $appRoot 'coreclr.dll')))) { throw 'The packaged executable did not prove app-local CoreCLR selection.' }
    }
    Write-Output 'PASS standalone portable: packaged EXE, app-local CoreCLR, light/dark WPF, isolated profiles; shared runtime search roots disabled; no hooks or visible window.'
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
