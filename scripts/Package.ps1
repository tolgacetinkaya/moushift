param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$packageRoot = Split-Path $PSScriptRoot -Parent
Push-Location $packageRoot
try {
    if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Build.ps1') -SelfContained }
    $project = [xml](Get-Content -LiteralPath 'src/Clickloom.App/Clickloom.App.csproj' -Raw)
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid package version.' }
    $binaryRoot = Join-Path $packageRoot ('artifacts/Moushift-' + $version + '-standalone')
    $payload = Join-Path '.local/package' ([guid]::NewGuid().ToString('N') + '/Moushift-' + $version)
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    foreach ($name in @('Moushift.exe','Moushift.dll','Clickloom.Core.dll','Clickloom.Windows.dll','Moushift.deps.json','Moushift.runtimeconfig.json','hostfxr.dll','hostpolicy.dll','coreclr.dll','System.Private.CoreLib.dll','PresentationFramework.dll')) {
        $source = Join-Path $binaryRoot $name
        if (-not (Test-Path -LiteralPath $source)) { throw "Missing release file: $name" }
    }
    $runtime = Get-Content -LiteralPath (Join-Path $binaryRoot 'Moushift.runtimeconfig.json') -Raw | ConvertFrom-Json
    if ($runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks -or 'Microsoft.NETCore.App' -notin $runtime.runtimeOptions.includedFrameworks.name -or 'Microsoft.WindowsDesktop.App' -notin $runtime.runtimeOptions.includedFrameworks.name) { throw 'The portable package must include both .NET and Windows Desktop runtimes.' }
    # Preserve runtime satellite directories and native libraries as published by the SDK.
    Get-ChildItem -LiteralPath $binaryRoot -File -Recurse | Where-Object Extension -ne '.pdb' | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($binaryRoot, $_.FullName)
        $target = Join-Path $payload $relative
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target
    }
    Copy-Item -LiteralPath 'README.md','LICENSE' -Destination $payload
    Copy-Item -LiteralPath 'docs/KNOWN_LIMITS.md' -Destination (Join-Path $payload 'KNOWN_LIMITS.md')
    Copy-Item -LiteralPath 'docs/INPUT_SUPPORT_SETUP.md' -Destination (Join-Path $payload 'INPUT_SUPPORT_SETUP.md')
    Copy-Item -LiteralPath 'docs/DRIVERLESS_CIRCLE.md' -Destination (Join-Path $payload 'DRIVERLESS_CIRCLE.md')
    & (Join-Path $PSScriptRoot 'Test-Portable.ps1') -AppDirectory $payload
    $output = Join-Path $packageRoot 'artifacts/release'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $archive = Join-Path $output ('Moushift-' + $version + '-win-x64.zip')
    Compress-Archive -LiteralPath $payload -DestinationPath $archive -Force
    $expectedFiles = @(Get-ChildItem -LiteralPath $payload -File -Recurse | ForEach-Object { [IO.Path]::GetRelativePath($payload, $_.FullName).Replace('\','/') })
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $entries = @($zip.Entries | Where-Object { $_.Name })
        if ($entries.Count -ne $expectedFiles.Count) { throw 'Unexpected portable ZIP contents.' }
        foreach ($entry in $entries) {
            $relativeEntry = $entry.FullName.Replace('\','/')
            $prefix = 'Moushift-' + $version + '/'
            if (-not $relativeEntry.StartsWith($prefix, [StringComparison]::Ordinal) -or $relativeEntry.Substring($prefix.Length) -notin $expectedFiles) { throw ('Unexpected ZIP path: ' + $entry.FullName) }
            $relativeFile = $relativeEntry.Substring($prefix.Length)
            $inputStream = $entry.Open()
            $hasher = [Security.Cryptography.SHA256]::Create()
            try { $entryHash = [Convert]::ToHexString($hasher.ComputeHash($inputStream)) }
            finally { $hasher.Dispose(); $inputStream.Dispose() }
            if ($entryHash -ne (Get-FileHash -LiteralPath (Join-Path $payload $relativeFile) -Algorithm SHA256).Hash) { throw ('ZIP file failed integrity check: ' + $relativeFile) }
        }
    } finally { $zip.Dispose() }
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), $hash + '  ' + (Split-Path $archive -Leaf) + "`n")
    Write-Output ('Portable preview: ' + $archive)
    Write-Output ('SHA256: ' + $hash)
} finally { Pop-Location }
