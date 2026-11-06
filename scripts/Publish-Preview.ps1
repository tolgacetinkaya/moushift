param([Parameter(Mandatory)][string]$Tag, [string]$ExpectedDraftCommit = '')
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$Tag = $Tag -replace '^release/', ''
if ($Tag -notmatch '^v\d+\.\d+\.\d+-preview\.\d+$') { throw 'Use a preview tag such as v0.14.1-preview.1.' }
$releaseRoot = Split-Path $PSScriptRoot -Parent
Push-Location $releaseRoot
try {
    $project = [xml](Get-Content src/Clickloom.App/Clickloom.App.csproj -Raw)
    $version = [string]$project.Project.PropertyGroup.Version
    if ($Tag -notmatch ('^v' + [regex]::Escape($version) + '-preview\.\d+$')) { throw 'Tag and app version differ.' }
    if ($env:GITHUB_SHA -notmatch '^[0-9a-f]{40}$' -or $env:GH_REPO -ne 'tolgacetinkaya/moushift') { throw 'Expected the Moushift release workflow and an exact commit.' }
    $notes = 'docs/releases/v' + $version + '-preview.md'
    $archive = 'artifacts/release/Moushift-' + $version + '-win-x64.zip'
    $checksum = 'artifacts/release/SHA256SUMS.txt'
    foreach ($path in @($notes, $archive, $checksum)) { if (-not (Test-Path -LiteralPath $path)) { throw "Missing release input: $path" } }
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    $expected = $hash + '  ' + (Split-Path $archive -Leaf)
    if ((Get-Content -LiteralPath $checksum -Raw).Trim() -ne $expected) { throw 'Archive checksum does not match.' }

    # A source update requires the exact previous draft commit, not a blanket overwrite.
    $existingText = gh release view $Tag --repo $env:GH_REPO --json isDraft,targetCommitish,url,tagName 2>$null
    $exists = $LASTEXITCODE -eq 0
    if ($exists) {
        $existing = $existingText | ConvertFrom-Json
        if (-not $existing.isDraft) { throw 'Published releases are never overwritten.' }
        if ($existing.targetCommitish -ne $env:GITHUB_SHA) {
            if ($ExpectedDraftCommit -notmatch '^[0-9a-f]{40}$' -or $existing.targetCommitish -ne $ExpectedDraftCommit) { throw 'The draft source changed; refusing to overwrite it.' }
            $tagText = gh api ('repos/' + $env:GH_REPO + '/git/ref/tags/' + $Tag) 2>$null
            if ($LASTEXITCODE -eq 0 -and ($tagText | ConvertFrom-Json).object.sha -ne $env:GITHUB_SHA) { throw 'The tag already points elsewhere; use a new preview number.' }
            gh release edit $Tag --repo $env:GH_REPO --target $env:GITHUB_SHA --draft --prerelease --title "Moushift $Tag" --notes-file $notes
            if ($LASTEXITCODE -ne 0) { throw 'Draft source update failed.' }
        }
    } else {
        gh release create $Tag --repo $env:GH_REPO --target $env:GITHUB_SHA --draft --prerelease --title "Moushift $Tag" --notes-file $notes
        if ($LASTEXITCODE -ne 0) { throw 'Draft release creation failed.' }
    }
    gh release upload $Tag $archive $checksum --repo $env:GH_REPO --clobber
    if ($LASTEXITCODE -ne 0) { throw 'Draft asset upload failed.' }
    gh release view $Tag --repo $env:GH_REPO --json url,isDraft,isPrerelease,tagName,assets
    if ($LASTEXITCODE -ne 0) { throw 'Draft verification failed.' }
} finally { Pop-Location }
