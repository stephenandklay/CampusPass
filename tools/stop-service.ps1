<#
.SYNOPSIS
    Stops the running CampusPass background service cleanly.

.DESCRIPTION
    Signals Local\CampusPass.Stop so the service flushes its "后台服务已停止" log line
    and releases the single-instance mutex, rather than being killed mid-request.
    Useful before rebuilding, since a running service locks outputs/CampusPass.exe.
#>
param([switch]$Restart)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class StopSignal {
  [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
  static extern IntPtr OpenEventW(uint access, bool inherit, string name);
  [DllImport("kernel32.dll")] static extern bool SetEvent(IntPtr h);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
  public static bool Signal() {
    IntPtr h = OpenEventW(0x10000002, false, "Local\\CampusPass.Stop");
    if (h == IntPtr.Zero) return false;
    bool ok = SetEvent(h);
    CloseHandle(h);
    return ok;
  }
}
'@

$running = @(Get-Process CampusPass -ErrorAction SilentlyContinue)
if ($running.Count -eq 0) { Write-Host 'no CampusPass instance is running'; return }

if ([StopSignal]::Signal()) {
    Write-Host 'signalled Local\CampusPass.Stop'
} else {
    Write-Host 'stop event not found; falling back to terminating the process'
    $running | Stop-Process -Force
}

$deadline = (Get-Date).AddSeconds(15)
while ((Get-Date) -lt $deadline -and @(Get-Process CampusPass -ErrorAction SilentlyContinue).Count -gt 0) {
    Start-Sleep -Milliseconds 500
}

$left = @(Get-Process CampusPass -ErrorAction SilentlyContinue)
if ($left.Count -gt 0) { Write-Host 'service did not exit within 15s; terminating'; $left | Stop-Process -Force }
Write-Host 'stopped.'

if ($Restart) {
    Start-Process -FilePath $running[0].Path -ArgumentList '--background'
    Start-Sleep -Seconds 3
    $up = @(Get-Process CampusPass -ErrorAction SilentlyContinue)
    Write-Host ("restarted: {0} instance(s)" -f $up.Count)
}
