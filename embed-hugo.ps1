# embed-hugo.ps1 - Embed hugo.exe into the publish directory
# Called by build.bat to avoid cmd inline PowerShell escaping issues.
# IMPORTANT: This file must remain ASCII-only (English) to avoid codepage issues.

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishDir  = Join-Path $projectRoot 'publish'
$targetHugo  = Join-Path $publishDir 'hugo.exe'

# Ensure publish directory exists
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

# 1. hugo.exe in project root (supports offline builds)
$localHugo = Join-Path $projectRoot 'hugo.exe'
if (Test-Path $localHugo) {
    Copy-Item $localHugo $targetHugo -Force
    Write-Host "    Copied local hugo.exe to publish directory"
    exit 0
}

# 2. vendor/hugo/hugo.exe (portable mode)
$vendorHugo = Join-Path $projectRoot 'vendor\hugo\hugo.exe'
if (Test-Path $vendorHugo) {
    Copy-Item $vendorHugo $targetHugo -Force
    Write-Host "    Copied vendor/hugo/hugo.exe to publish directory"
    exit 0
}

# 3. Skip download if hugo.exe already embedded (idempotent)
if (Test-Path $targetHugo) {
    Write-Host "    hugo.exe already present: $targetHugo"
    exit 0
}

# 4. Otherwise download latest stable Hugo (windows-amd64) from GitHub
$version = '0.145.0'
$url     = "https://github.com/gohugoio/hugo/releases/download/v$version/hugo_${version}_windows-amd64.zip"
$tempBase = (Get-Item $env:TEMP).FullName
$zip     = Join-Path $tempBase 'hugo_download.zip'
$extract = Join-Path $tempBase ('hugo_extract_' + [guid]::NewGuid().ToString('N'))

try {
    Write-Host "    Downloading Hugo v$version from GitHub..."
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing

    Write-Host "    Extracting hugo.exe..."
    Expand-Archive -Path $zip -DestinationPath $extract -Force
    $hugoBin = Get-ChildItem -Path $extract -Filter 'hugo.exe' -Recurse | Select-Object -First 1
    if (-not $hugoBin) {
        throw 'hugo.exe not found in downloaded archive'
    }
    Copy-Item $hugoBin.FullName $targetHugo -Force
    Write-Host "    Hugo embedded successfully: $targetHugo"
}
catch {
    Write-Host "    [WARN] Hugo could not be embedded automatically: $($_.Exception.Message)"
    Write-Host "          To use this app on computers without Hugo installed:"
    Write-Host "          1. Download hugo.exe from https://gohugo.io/installation/windows/"
    Write-Host "          2. Place it in the project root (hugo.exe) and rerun build.bat"
    Write-Host "          Otherwise the app will fall back to Hugo in system PATH."
}
finally {
    # Use .NET APIs to avoid PowerShell path-resolution issues (e.g. 8.3 short temp paths)
    try {
        if ($zip -and [System.IO.File]::Exists($zip)) {
            [System.IO.File]::Delete($zip)
        }
        if ($extract -and [System.IO.Directory]::Exists($extract)) {
            [System.IO.Directory]::Delete($extract, $true)
        }
    } catch {
        # Best-effort cleanup; never fail the build for temp-file issues
    }
}

exit 0