param([string]$Destination)
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($Destination)) { $Destination = Join-Path $sourceRoot ('.local/public-review/clickloom-' + (Get-Date -Format yyyyMMdd-HHmmss)) }
$targetRoot = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $targetRoot) { throw 'Use a new, empty destination; existing work will not be overwritten.' }
if (-not $targetRoot.StartsWith(([IO.Path]::GetFullPath((Join-Path $sourceRoot '.local/public-review')) + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) { throw 'The public review copy must stay under .local/public-review.' }
New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
function Copy-PublicFile([string]$relative) {
    $from = Join-Path $sourceRoot $relative
    $to = Join-Path $targetRoot $relative
    New-Item -ItemType Directory -Path (Split-Path $to -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $from -Destination $to
}
foreach ($relative in @('README.md','LICENSE','CONTRIBUTING.md','SECURITY.md','.gitignore','Directory.Build.props','Clickloom.slnx','Start-Clickloom.cmd')) { Copy-PublicFile $relative }
foreach ($folder in @('src','tests','.github')) {
    Get-ChildItem -LiteralPath (Join-Path $sourceRoot $folder) -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.dotnet-home|\.local|\.git)[\\/]' } | ForEach-Object {
        Copy-PublicFile $_.FullName.Substring($sourceRoot.Length + 1)
    }
}
foreach ($relative in @('scripts/Build.ps1','scripts/Verify.ps1','scripts/Package.ps1','scripts/Prepare-Public.ps1','docs/HID_LEARNING.md','docs/KNOWN_LIMITS.md','docs/releases/v0.12.0-preview.1.md','docs/ui-012/learning-copper.png','docs/ui-012/learning-pink.png')) { Copy-PublicFile $relative }
$files = Get-ChildItem -LiteralPath $targetRoot -Recurse -File
if ($files | Where-Object { $_.FullName.Substring($targetRoot.Length) -match '[\\/](research|artifacts|bin|obj)[\\/]|profiles\.json|appearance\.json|\.asar$|\.pdb$' }) { throw 'Unexpected private/build artifact in public review copy.' }
Write-Output $targetRoot
Write-Output ('Public review files: ' + $files.Count)
