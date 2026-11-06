param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$packageRoot = Split-Path $PSScriptRoot -Parent
Push-Location $packageRoot
try {
    if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Build.ps1') }
    $project = [xml](Get-Content -LiteralPath 'src/Clickloom.App/Clickloom.App.csproj' -Raw)
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid package version.' }
    $binaryRoot = Join-Path 'artifacts' ('Moushift-' + $version)
    $payload = Join-Path '.local/package' ([guid]::NewGuid().ToString('N') + '/Moushift-' + $version)
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    foreach ($name in @('Moushift.exe','Moushift.dll','Clickloom.Core.dll','Clickloom.Windows.dll','Moushift.deps.json','Moushift.runtimeconfig.json')) {
        $source = Join-Path $binaryRoot $name
        if (-not (Test-Path -LiteralPath $source)) { throw "Missing release file: $name" }
        Copy-Item -LiteralPath $source -Destination $payload
    }
    Copy-Item -LiteralPath 'README.md','LICENSE' -Destination $payload
    Copy-Item -LiteralPath 'docs/KNOWN_LIMITS.md' -Destination (Join-Path $payload 'KNOWN_LIMITS.md')
    Copy-Item -LiteralPath 'docs/INPUT_SUPPORT_SETUP.md' -Destination (Join-Path $payload 'INPUT_SUPPORT_SETUP.md')
    Copy-Item -LiteralPath 'docs/DRIVERLESS_CIRCLE.md' -Destination (Join-Path $payload 'DRIVERLESS_CIRCLE.md')
    $output = Join-Path $packageRoot 'artifacts/release'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $archive = Join-Path $output ('Moushift-' + $version + '-win-x64.zip')
    Compress-Archive -LiteralPath $payload -DestinationPath $archive -Force
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), $hash + '  ' + (Split-Path $archive -Leaf) + "`n")
    Write-Output ('Portable preview: ' + $archive)
    Write-Output ('SHA256: ' + $hash)
} finally { Pop-Location }
