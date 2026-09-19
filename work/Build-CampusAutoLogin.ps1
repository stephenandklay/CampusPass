$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'CampusAutoLogin.cs'
# Deliberately NOT outputs\CampusFlow.exe: that is the shipped WPF build, and the
# HKCU Run key points at it. Writing here would silently downgrade a working
# install to the legacy WinForms binary.
$output = Join-Path $PSScriptRoot 'CampusFlow-legacy.exe'
$icon = Join-Path $PSScriptRoot 'assets\CampusPass.ico'

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Force
}

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
& $csc /nologo /target:winexe /out:$output /win32icon:$icon /reference:System.dll,System.Core.dll,System.Drawing.dll,System.Security.dll,System.Xml.dll,System.Windows.Forms.dll $source
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }

Write-Host "Built: $output"
