#Requires -Version 7.0
<#
.SYNOPSIS
    Builds AMPLE, signs the exe and uploads it to a draft GitHub release, with an optional offline bundle.

.DESCRIPTION
    Run from anywhere; the script works in the repository root. Steps:
      1. reads the version from Directory.Build.props and its section from CHANGELOG.md,
      2. checks formatting and publishes the self-contained single-file AMPLE.exe,
      3. with -Sign, signs it with the code-signing certificate in your certificate store (Certum through
         SimplySign Desktop, which must be running and logged in) and verifies the signature,
      4. zips the exe (the online installer) and writes a SHA-256 file next to it in artifacts\,
      5. with -Bundle, also downloads every component in manifest\manifest.json into a bundle\ folder next to
         the exe and zips that as the offline installer,
      6. with -Upload, creates a draft release for the tag (or updates the existing draft) with the changelog
         section as its notes. Publish it by hand on GitHub when you're ready.

.PARAMETER Sign
    Sign AMPLE.exe before packaging. Without it the build is unsigned and the notes say so.

.PARAMETER Bundle
    Also build the offline zip with every component inside.

.PARAMETER Upload
    Create or update the draft release on GitHub. Needs the GitHub CLI; set the token with
    $env:GITHUB_TOKEN = gh auth token

.PARAMETER CertificateThumbprint
    The signing certificate's thumbprint. Without it, the newest valid code-signing certificate in
    Cert:\CurrentUser\My is used.

.EXAMPLE
    .\tools\release.ps1 -Sign -Bundle -Upload
#>
[CmdletBinding()]
param(
    [switch] $Sign,
    [switch] $Bundle,
    [switch] $Upload,
    [string] $CertificateThumbprint
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$timestampServer = 'http://time.certum.pl'
$repository = 'ClearanceClarence-Software/AMPLE'

function Write-Step([string] $text) {
    Write-Host ""
    Write-Host "== $text" -ForegroundColor Cyan
}

function Get-ProjectVersion {
    [xml] $props = Get-Content (Join-Path $repoRoot 'Directory.Build.props')
    $version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Directory.Build.props has no <Version> like 1.2.3 (found '$version')."
    }
    return $version
}

function Get-ChangelogSection([string] $version) {
    $lines = @()
    $inSection = $false
    foreach ($line in Get-Content (Join-Path $repoRoot 'CHANGELOG.md')) {
        if ($line -match '^## \[(.+?)\]') {
            if ($inSection) { break }
            $inSection = $Matches[1] -eq $version
            continue
        }
        if ($inSection) { $lines += $line }
    }
    $text = ($lines -join "`n").Trim()
    if (-not $text) {
        throw "CHANGELOG.md has no section for $version. Add '## [$version] - <date>' first."
    }
    return $text
}

function Find-SignTool {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $signTool = Get-ChildItem $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $signTool) {
        throw 'signtool.exe not found. Install the Windows SDK (the "Signing Tools" feature is enough).'
    }
    return $signTool.FullName
}

function Find-Certificate([string] $thumbprint) {
    if ($thumbprint) {
        $certificate = Get-Item "Cert:\CurrentUser\My\$thumbprint" -ErrorAction SilentlyContinue
    }
    else {
        $certificate = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
            Where-Object { $_.NotAfter -gt (Get-Date) } |
            Sort-Object NotBefore -Descending |
            Select-Object -First 1
    }
    if (-not $certificate) {
        throw 'No valid code-signing certificate found. Start SimplySign Desktop and log in, then try again.'
    }
    return $certificate
}

function Write-Hash([string] $file) {
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path $file -Leaf)" | Set-Content "$file.sha256" -Encoding ascii -NoNewline
    Write-Host "$file"
    Write-Host "SHA-256 $hash"
    return $hash
}

$version = Get-ProjectVersion
$tag = "v$version"
$changelog = Get-ChangelogSection $version
Write-Host "Releasing AMPLE $tag" -ForegroundColor Green

Write-Step 'Checking formatting'
dotnet format AMPLE.sln --verify-no-changes
if ($LASTEXITCODE -ne 0) { throw 'dotnet format found changes. Run dotnet format and commit them first.' }

Write-Step 'Publishing'
$publishDir = Join-Path $repoRoot 'bin\publish\win-x64'
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish src\Ample.App -p:PublishProfile=win-x64 -p:Version=$version
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
$exe = Join-Path $publishDir 'AMPLE.exe'

$signedBy = $null
if ($Sign) {
    Write-Step 'Signing'
    $certificate = Find-Certificate $CertificateThumbprint
    Write-Host "Certificate: $($certificate.Subject) (valid until $($certificate.NotAfter.ToString('yyyy-MM-dd')))"
    & (Find-SignTool) sign /sha1 $certificate.Thumbprint /fd SHA256 /tr $timestampServer /td SHA256 `
        /d 'AMPLE' /du "https://github.com/$repository" $exe
    if ($LASTEXITCODE -ne 0) { throw 'signtool failed. Is SimplySign Desktop running and logged in?' }

    $signature = Get-AuthenticodeSignature $exe
    if ($signature.Status -ne 'Valid') {
        throw "The signature isn't valid: $($signature.StatusMessage)"
    }
    $signedBy = $signature.SignerCertificate.GetNameInfo('SimpleName', $false)
    Write-Host "Signed by $signedBy" -ForegroundColor Green
}

Write-Step 'Packaging the online installer'
$artifacts = Join-Path $repoRoot 'artifacts'
New-Item $artifacts -ItemType Directory -Force | Out-Null
$zip = Join-Path $artifacts "AMPLE-$tag-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $exe -DestinationPath $zip
$hash = Write-Hash $zip
$assets = @($zip, "$zip.sha256")

$offlineHash = $null
if ($Bundle) {
    Write-Step 'Building the offline bundle'
    $bundleRoot = Join-Path $artifacts 'offline'
    if (Test-Path $bundleRoot) { Remove-Item $bundleRoot -Recurse -Force }
    New-Item (Join-Path $bundleRoot 'bundle') -ItemType Directory -Force | Out-Null
    Copy-Item $exe $bundleRoot
    dotnet run --project tools\Ample.Manifest -- bundle (Join-Path $bundleRoot 'bundle')
    if ($LASTEXITCODE -ne 0) { throw 'Building the bundle failed. Does every manifest checksum match? Run: dotnet run --project tools/Ample.Manifest -- verify' }
    $offlineZip = Join-Path $artifacts "AMPLE-$tag-win-x64-offline.zip"
    if (Test-Path $offlineZip) { Remove-Item $offlineZip -Force }
    Compress-Archive -Path (Join-Path $bundleRoot '*') -DestinationPath $offlineZip
    $offlineHash = Write-Hash $offlineZip
    $assets += @($offlineZip, "$offlineZip.sha256")
}

$notes = $changelog
if ($signedBy) {
    $notes += "`n`n> AMPLE.exe is code-signed by **$signedBy**."
}
else {
    $notes += "`n`n> This build is not code-signed, so Windows SmartScreen may warn the first time you run it."
}
$notes += "`n`n**Online installer** (downloads the components, ~10 MB): SHA-256 ``$hash``"
if ($offlineHash) {
    $notes += "`n`n**Offline installer** (everything inside): SHA-256 ``$offlineHash``"
}
$notesFile = Join-Path $artifacts "release-notes-$tag.md"
$notes | Set-Content $notesFile -Encoding utf8

if (-not $Upload) {
    Write-Step 'Done'
    Write-Host "Not uploaded. Run again with -Upload to create the draft release, or upload the artifacts by hand."
    return
}

Write-Step 'Uploading to a draft release'
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'The GitHub CLI (gh) is needed for -Upload: winget install GitHub.cli'
}

gh release view $tag --repo $repository *> $null
if ($LASTEXITCODE -eq 0) {
    $isDraft = gh release view $tag --repo $repository --json isDraft --jq .isDraft
    if ($isDraft -ne 'true') {
        throw "Release $tag is already published. Bump the version instead of replacing a published build."
    }
    gh release upload $tag @assets --repo $repository --clobber
    gh release edit $tag --repo $repository --notes-file $notesFile
}
else {
    gh release create $tag @assets --repo $repository --draft --title "$tag" --notes-file $notesFile --target main
}
if ($LASTEXITCODE -ne 0) { throw 'Uploading to GitHub failed.' }

Write-Step 'Done'
Write-Host "Draft release $tag is ready. Check it and publish it on GitHub:"
Write-Host "https://github.com/$repository/releases"
