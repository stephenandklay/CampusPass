$ErrorActionPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

Write-Host '--- HKCU Run key (CampusPass entries) ---'
$run = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ($run) {
    $found = $false
    foreach ($prop in $run.PSObject.Properties) {
        if ($prop.Name -match 'Campus') { Write-Host ("  {0} = {1}" -f $prop.Name, $prop.Value); $found = $true }
    }
    if (-not $found) { Write-Host '  (none)' }
}

Write-Host '--- running CampusPass processes ---'
$procs = @(Get-Process CampusPass)
if ($procs.Count -eq 0) { Write-Host '  (none running)' }
foreach ($proc in $procs) {
    Write-Host ("  pid {0}  started {1}  PM {2} MB  path {3}" -f `
        $proc.Id, $proc.StartTime, [math]::Round($proc.PM / 1MB, 1), $proc.Path)
}

Write-Host '--- data directory ---'
$dir = Join-Path $env:LOCALAPPDATA 'CampusPass'
foreach ($f in Get-ChildItem $dir) {
    Write-Host ("  {0}  {1} bytes  {2}" -f $f.Name, $f.Length, $f.LastWriteTime)
}

Write-Host '--- background service alive? (named mutex held) ---'
$held = $false
$m = New-Object System.Threading.Mutex($false, 'Local\CampusPass.SingleInstance', [ref]$held)
Write-Host ("  mutex created-new={0}  => service running: {1}" -f $held, (-not $held))
$m.Dispose()
