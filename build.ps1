#Requires -Version 5.1
<#
.SYNOPSIS
    Builds PakEditor-FM and assembles the full distributable output.

.PARAMETER Configuration
    Build configuration: Debug or Release (default: Release)

.PARAMETER NoPause
    Skip the pause at the end (useful for CI)

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Configuration Debug
    .\build.ps1 -Configuration Release -NoPause
#>

param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',
    [switch]$NoPause
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root   = $PSScriptRoot
$out    = Join-Path $root "publish\$($Configuration.ToLower())"
$fmodel = Join-Path $root "FModel"

function Copy-IfExists([string]$src, [string]$dst) {
    if (Test-Path $src) {
        $dir = Split-Path $dst -Parent
        if ($dir -and !(Test-Path $dir)) { New-Item $dir -ItemType Directory -Force | Out-Null }
        Copy-Item $src $dst -Force
        Write-Host "  copied  $(Split-Path $src -Leaf)"
    } else {
        Write-Warning "  MISSING $src"
    }
}

# -------------------------------------------------------------------------
# Step 1 - Build
# -------------------------------------------------------------------------
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  PakEditor-FM  --  $Configuration build"                        -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Step 1/4  Building ($Configuration)..." -ForegroundColor Yellow

Push-Location $fmodel
try {
    if ($Configuration -eq 'Release') {
        dotnet publish FModel.csproj `
            -c Release -r win-x64 `
            --self-contained true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -o $out
    } else {
        dotnet publish FModel.csproj `
            -c Debug -r win-x64 `
            --no-self-contained `
            -o $out
    }
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
} finally {
    Pop-Location
}

Write-Host "  Build OK -> $out" -ForegroundColor Green

# -------------------------------------------------------------------------
# Step 2 - Bundle UAssetGUI (portable, v1.0.4+)
# -------------------------------------------------------------------------
Write-Host ""
Write-Host "Step 2/4  Bundling UAssetGUI..." -ForegroundColor Yellow

$uagSrc = Join-Path $root "UAssetGUI"
$uagDst = Join-Path $out  "UAssetGUI"
New-Item $uagDst -ItemType Directory -Force | Out-Null

Copy-IfExists (Join-Path $uagSrc "UAssetGUI.exe") (Join-Path $uagDst "UAssetGUI.exe")
Copy-IfExists (Join-Path $uagSrc "LICENSE")        (Join-Path $uagDst "LICENSE")
Copy-IfExists (Join-Path $uagSrc "NOTICE.md")      (Join-Path $uagDst "NOTICE.md")
Copy-IfExists (Join-Path $uagSrc "README.md")      (Join-Path $uagDst "README.md")

if (!(Test-Path (Join-Path $uagDst "UAssetGUI.exe"))) {
    Write-Warning "UAssetGUI.exe missing. Download v1.0.4+ from https://github.com/atenfyr/UAssetGUI/releases and place it in UAssetGUI\"
}

# -------------------------------------------------------------------------
# Step 3 - Bundle retoc
# -------------------------------------------------------------------------
Write-Host ""
Write-Host "Step 3/4  Bundling retoc..." -ForegroundColor Yellow

$retocSrc = Join-Path $root "Retoc"
$retocDst = Join-Path $out  "Retoc"
New-Item $retocDst -ItemType Directory -Force | Out-Null

Copy-IfExists (Join-Path $retocSrc "retoc.exe")  (Join-Path $retocDst "retoc.exe")
Copy-IfExists (Join-Path $retocSrc "LICENSE")     (Join-Path $retocDst "LICENSE")
Copy-IfExists (Join-Path $retocSrc "README.md")   (Join-Path $retocDst "README.md")

# -------------------------------------------------------------------------
# Step 4 - Copy docs and licenses
# -------------------------------------------------------------------------
Write-Host ""
Write-Host "Step 4/4  Copying docs and licenses..." -ForegroundColor Yellow

Copy-IfExists (Join-Path $root "README.md") (Join-Path $out "README.md")
Copy-IfExists (Join-Path $root "LICENSE")   (Join-Path $out "LICENSE")
Copy-IfExists (Join-Path $root "NOTICE")    (Join-Path $out "NOTICE")

$licDst = Join-Path $out "licenses"
New-Item $licDst -ItemType Directory -Force | Out-Null

# FModel (GPL-3)
Copy-IfExists (Join-Path $root "LICENSE")          (Join-Path $licDst "GPL-3.0-FModel.txt")
Copy-IfExists (Join-Path $root "NOTICE")           (Join-Path $licDst "NOTICE-FModel.txt")

# UAssetGUI (MIT)
Copy-IfExists (Join-Path $root "UAssetGUI\LICENSE")    (Join-Path $licDst "MIT-UAssetGUI.txt")
Copy-IfExists (Join-Path $root "UAssetGUI\NOTICE.md")  (Join-Path $licDst "NOTICE-UAssetGUI.md")

# UAssetAPI (MIT)
Copy-IfExists (Join-Path $root "UAssetAPI\LICENSE")    (Join-Path $licDst "MIT-UAssetAPI.txt")
Copy-IfExists (Join-Path $root "UAssetAPI\NOTICE.md")  (Join-Path $licDst "NOTICE-UAssetAPI.md")

# CUE4Parse (Apache 2.0)
Copy-IfExists (Join-Path $root "CUE4Parse\LICENSE")    (Join-Path $licDst "Apache-2.0-CUE4Parse.txt")
Copy-IfExists (Join-Path $root "CUE4Parse\NOTICE")     (Join-Path $licDst "NOTICE-CUE4Parse.txt")

# retoc (MIT)
Copy-IfExists (Join-Path $root "Retoc\LICENSE")        (Join-Path $licDst "MIT-retoc.txt")

# -------------------------------------------------------------------------
# Done
# -------------------------------------------------------------------------

# Check for oo2core — needed by PakEditor.exe for Oodle decompression
$oodle = Join-Path $out "oo2core_9_win64.dll"
if (!(Test-Path $oodle)) {
    Write-Host ""
    Write-Warning "oo2core_9_win64.dll is missing from the output folder."
    Write-Host "  Copy it from your Unreal Engine installation or game files:" -ForegroundColor Yellow
    Write-Host "  e.g. UE_5.x\Engine\Binaries\Win64\oo2core_9_win64.dll"     -ForegroundColor Yellow
    Write-Host "  and place it next to PakEditor.exe before running."          -ForegroundColor Yellow
}

Write-Host ""
Write-Host "================================================================" -ForegroundColor Green
Write-Host "  Done!  Output: $out"                                           -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  PakEditor.exe          main application"
Write-Host "  UAssetGUI\             portable UAssetGUI (v1.0.4+)"
Write-Host "  Retoc\                 retoc IoStore converter"
Write-Host "  README.md              user guide"
Write-Host "  LICENSE / NOTICE       PakEditor-FM (GPL-3)"
Write-Host "  licenses\              all third-party license texts"
Write-Host ""

if (!$NoPause) { pause }
