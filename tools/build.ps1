<#
.SYNOPSIS
    Builds and publishes CampusPass as a single-file self-contained win-x64 exe.

.DESCRIPTION
    Replaces the legacy work/Build-CampusAutoLogin.ps1 (csc.exe on .NET Framework).
    Publishes to outputs\CampusPass.exe. Note that the rename from CampusFlow changed
    both the exe filename and the HKCU Run value name, so an install registered under
    the old name is NOT picked up by path alone - opening the GUI once repoints it.
#>
param(
    [switch]$Compress,           # smaller exe, but assemblies must be decompressed into memory at startup
    [switch]$FrameworkDependent, # tiny exe, requires the .NET 8 Desktop Runtime on the target machine
    [switch]$SkipPublish,        # build + boundary check only
    [switch]$FreshIcon,
    [switch]$Tests                 # run the Core regression checks before publishing
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$root      = Split-Path $PSScriptRoot -Parent   # repo root: the parent of tools/
$project   = Join-Path $root 'app\src\CampusPass'
$csproj    = Join-Path $project 'CampusPass.csproj'
$publishTo = Join-Path $root 'app\publish'
$outDir    = Join-Path $root 'outputs'
$exeOut    = Join-Path $outDir 'CampusPass.exe'

function Step([string]$m) { Write-Host "`n==> $m" -ForegroundColor Cyan }

# ------------------------------------------------------------------ 1. preflight
Step 'Checking the toolchain'
$sdkLine = ''
try { $sdkLine = (& dotnet --list-sdks) 2>$null | Select-Object -Last 1 } catch { }
if (-not $sdkLine) {
    throw @'
No .NET SDK found. Install it, then re-run this script:

    winget install --id Microsoft.DotNet.SDK.8 -e --accept-package-agreements --accept-source-agreements
'@
}
Write-Host "  SDK $sdkLine"

# ---------------------------------------------- 2. WPF must stay out of Core/
Step 'Enforcing the memory boundary (Core/ must not reference WPF)'
$forbidden = 'using\s+System\.Windows|using\s+Wpf\.Ui|using\s+System\.Drawing'
$violations = @()
foreach ($file in Get-ChildItem (Join-Path $project 'Core') -Filter *.cs) {
    if (Select-String -Path $file.FullName -Pattern $forbidden -Quiet) {
        $violations += $file.Name
    }
}
if (Select-String -Path (Join-Path $project 'Program.cs') -Pattern $forbidden -Quiet) {
    $violations += 'Program.cs'
}
if ($violations.Count -gt 0) {
    throw ("Core/Program.cs must stay BCL-only so --background never loads WPF. Offenders: " + ($violations -join ', '))
}
Write-Host '  clean'

# ------------------------------------------------------------- 3. regression
if ($Tests) {
    Step 'Running the Core regression checks'
    $testProj = Join-Path $root 'tests\CoreChecks\CoreChecks.csproj'
    dotnet build $testProj -c Release --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'test build failed' }
    & (Join-Path (Split-Path $testProj) 'bin/Release/net8.0-windows/CampusPass.CoreChecks.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Core regression checks failed' }
}

# -------------------------------------------------------------------- 4. icon
if ($FreshIcon) {
    Step 'Re-mastering the icon'
    python (Join-Path $PSScriptRoot 'make_icon.py')
    if ($LASTEXITCODE -ne 0) { throw 'make_icon.py failed' }
}

# ----------------------------------------------------------------- 5. publish
if ($SkipPublish) {
    Step 'Build only'
    dotnet build $csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }
    return
}

Step 'Publishing'
if (Test-Path $publishTo) { Remove-Item $publishTo -Recurse -Force }

$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
# Build each -p: argument as a whole string first: inside @(), the comma binds
# tighter than +, which would silently split 'x=' + $v into two array elements.
$compressionProp = '-p:EnableCompressionInSingleFile=' + $(if ($Compress) { 'true' } else { 'false' })
# WPF ships five native dlls (wpfgfx, PresentationNative, PenImc, D3DCompiler,
# vcruntime140). Without this they are published loose and the result is not a
# single file; with it they embed and unpack to %TEMP% on first run.
$selfExtractProp = '-p:IncludeNativeLibrariesForSelfExtract=true'
$arguments = @(
    'publish', $csproj,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', $selfContained,
    '-p:PublishSingleFile=true',
    $compressionProp,
    $selfExtractProp,
    '-p:DebugType=embedded',
    '-o', $publishTo,
    '--nologo'
)
Write-Host "  dotnet $($arguments -join ' ')"
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "publish failed with exit code $LASTEXITCODE" }

$built = Get-ChildItem $publishTo -Filter CampusPass.exe | Select-Object -First 1
if (-not $built) { throw "no CampusPass.exe in $publishTo" }
$loose = @(Get-ChildItem $publishTo -File | Where-Object { $_.Name -ne 'CampusPass.exe' })
if ($loose.Count -gt 0) {
    Write-Host ("  WARNING: {0} loose files next to the exe: {1}" -f `
        $loose.Count, (($loose | Select-Object -First 8 -ExpandProperty Name) -join ', ')) -ForegroundColor Yellow
}

# --------------------------------------------------------------- 6. artifacts
Step 'Writing artifacts'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory $outDir | Out-Null }
Copy-Item $built.FullName $exeOut -Force

$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exeOut).FileVersion
$zip     = Join-Path $outDir "CampusPass-v$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $exeOut -DestinationPath $zip

# ------------------------------------------------------------------ 7. report
Step 'Result'
$mb = { param($b) [math]::Round($b / 1MB, 1) }
Write-Host ("  exe      {0}  ({1} MB)" -f $exeOut, (& $mb (Get-Item $exeOut).Length))
Write-Host ("  zip      {0}  ({1} MB)" -f $zip,   (& $mb (Get-Item $zip).Length))
Write-Host ("  version  {0}" -f $version)
Write-Host ("  mode     {0}{1}" -f $(if ($FrameworkDependent) { 'framework-dependent' } else { 'self-contained' }),
                                           $(if ($Compress) { ', compressed' } else { ', uncompressed (lower RAM)' }))
Write-Host "`nNext: powershell -File tools/verify-background.ps1 -exe `"$exeOut`""
