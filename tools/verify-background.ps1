param([Parameter(Mandatory = $true)][string]$exe,
     [switch]$Force)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;
public static class StopSignal {
  [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
  static extern IntPtr OpenEventW(uint access, bool inherit, string name);
  [DllImport("kernel32.dll", SetLastError=true)]
  static extern IntPtr CreateEventW(IntPtr sa, bool manualReset, bool initialState, string name);
  [DllImport("kernel32.dll", SetLastError=true)]
  static extern bool SetEvent(IntPtr h);
  [DllImport("kernel32.dll", SetLastError=true)]
  static extern bool CloseHandle(IntPtr h);
  [DllImport("user32.dll")]
  static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

  public static bool Signal() {
    IntPtr h = OpenEventW(0x10000002, false, "Local\\CampusPass.Stop");
    if (h == IntPtr.Zero) h = CreateEventW(IntPtr.Zero, true, false, "Local\\CampusPass.Stop");
    if (h == IntPtr.Zero) return false;
    bool ok = SetEvent(h);
    CloseHandle(h);
    return ok;
  }
  public static bool AlreadyExists() {
    IntPtr h = OpenEventW(0x10000002, false, "Local\\CampusPass.Stop");
    if (h == IntPtr.Zero) return false;
    CloseHandle(h); return true;
  }
}
'@

function P($p) { [math]::Round($p.PM / 1MB, 1) }

# These checks stop and restart the service via the shared named event, so they
# would take down an instance the user started themselves.
$already = @(Get-Process CampusPass -ErrorAction SilentlyContinue)
if ($already.Count -gt 0 -and -not $Force) {
    Write-Host ("A CampusPass instance is already running (pid {0}); this script would stop it." -f ($already.Id -join ', '))
    Write-Host "Re-run with -Force to take it over, or stop it from the app first."
    exit 2
}


Write-Host "== launching background service =="
Start-Process -FilePath $exe -ArgumentList '--background' -WindowStyle Hidden
Start-Sleep -Seconds 6

$procs = @(Get-Process CampusPass -ErrorAction SilentlyContinue)
Write-Host ("instances after first launch : {0}" -f $procs.Count)
if ($procs.Count -ne 1) { Write-Host "FAIL: expected exactly one instance"; exit 1 }
$proc = $procs[0]
Write-Host ("pid {0}  PrivateMB {1}  WorkingSetMB {2}  CPUsec {3}" -f `
    $proc.Id, (P $proc), ([math]::Round($proc.WS / 1MB, 1)), ([math]::Round($proc.CPU, 2)))

# The HKCU Run key is written from Environment.ProcessPath. Under single-file
# publishing that must resolve to the real exe, never into %TEMP%\.NET\.
Write-Host "`n== executable path resolution (Run-key safety) =="
$real = (Resolve-Path $exe).Path
Write-Host ("  launched    : {0}" -f $real)
Write-Host ("  ProcessPath : {0}" -f $proc.Path)
if ($proc.Path -ne $real) { Write-Host "FAIL: process path is not the real exe"; exit 1 }
if ($proc.Path -like ($env:TEMP + '*')) { Write-Host "FAIL: path leaked into TEMP"; exit 1 }
Write-Host "PASS: the Run key will point at the real exe"

Write-Host "`n== loaded modules that must NOT be present =="
$mods = @($proc.Modules | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_.FileName) })
$bad = @($mods | Where-Object { $_ -match '^(PresentationFramework|PresentationCore|WindowsBase|Wpf\.Ui|Wpf\.Ui\.Abstractions|System\.Windows\.Controls|System\.Xaml|DirectWriteForwarder|PresentationNative)' })
if ($bad.Count -gt 0) { Write-Host ("FAIL: WPF assemblies loaded in background: {0}" -f ($bad -join ', ')); exit 1 }
Write-Host "PASS: no WPF assemblies loaded (of $($mods.Count) modules)"
$guiOnly = @($mods | Where-Object { $_ -match 'CampusPass' })
Write-Host ("host exe module present    : {0}" -f ($guiOnly.Count -gt 0))

Write-Host "`n== single instance guard =="
Start-Process -FilePath $exe -ArgumentList '--background' -WindowStyle Hidden
Start-Sleep -Seconds 3
$after = @(Get-Process CampusPass -ErrorAction SilentlyContinue)
Write-Host ("instances after 2nd launch   : {0} (expect 1)" -f $after.Count)
if ($after.Count -ne 1) { Write-Host "FAIL: mutex did not block the second instance"; exit 1 }
Write-Host "PASS: second instance exited on its own"

Write-Host "`n== idle CPU while parked =="
$c1 = (Get-Process -Id $proc.Id).CPU
Start-Sleep -Seconds 10
$c2 = (Get-Process -Id $proc.Id).CPU
$delta = [math]::Round($c2 - $c1, 3)
Write-Host ("CPU seconds consumed in 10s idle: {0}" -f $delta)

Write-Host "`n== graceful stop =="
[void][StopSignal]::Signal()
$proc.WaitForExit(8000) | Out-Null
if (-not $proc.HasExited) { Write-Host "FAIL: service did not stop on the event"; exit 1 }
Write-Host "PASS: service exited on Local\CampusPass.Stop"

Write-Host "`n== log file =="
$log = Join-Path $env:LOCALAPPDATA 'CampusPass\campuspass.log'
if (Test-Path $log) {
    $bytes = [IO.File]::ReadAllBytes($log)
    Write-Host ("size {0} bytes, first 3 bytes: {1}" -f $bytes.Length, (($bytes[0..([Math]::Min(2,$bytes.Length-1))] | ForEach-Object { $_.ToString('X2') }) -join ' '))
    Get-Content $log -Encoding UTF8 | Select-Object -Last 6 | ForEach-Object { Write-Host "  | $_" }
} else { Write-Host "FAIL: no log written"; exit 1 }

Write-Host "`nALL BACKGROUND CHECKS PASSED"
