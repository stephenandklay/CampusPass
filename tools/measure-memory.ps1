param([Parameter(Mandatory = $true)][string]$exe,
     [Parameter(Mandatory = $true)][string]$label,
     [int[]]$atSeconds = @(5, 35, 95),
     [switch]$Force)

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;
public static class Sig {
  [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
  static extern IntPtr OpenEventW(uint access, bool inherit, string name);
  [DllImport("kernel32.dll", SetLastError=true)]
  static extern bool SetEvent(IntPtr h);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
  public static void Stop() {
    IntPtr h = OpenEventW(0x10000002, false, "Local\\CampusPass.Stop");
    if (h != IntPtr.Zero) { SetEvent(h); CloseHandle(h); }
  }
}
'@

function Sample($proc) {
    $proc.Refresh()
    [pscustomobject]@{
        PrivateMB  = [math]::Round($proc.PrivateMemorySize64 / 1MB, 1)
        WorkingMB  = [math]::Round($proc.WorkingSet64 / 1MB, 1)
        PeakWorkMB = [math]::Round($proc.PeakWorkingSet64 / 1MB, 1)
        MinWorkMB  = [math]::Round($proc.NonpagedSystemMemorySize64 / 1KB, 0)
        CPUsec     = [math]::Round($proc.CPU, 2)
        Modules    = @($proc.Modules).Count
    }
}

# These checks stop and restart the service via the shared named event, so they
# would take down an instance the user started themselves.
$already = @(Get-Process CampusPass -ErrorAction SilentlyContinue)
if ($already.Count -gt 0 -and -not $Force) {
    Write-Host ("A CampusPass instance is already running (pid {0}); this script would stop it." -f ($already.Id -join ', '))
    Write-Host "Re-run with -Force to take it over, or stop it from the app first."
    exit 2
}

# Make sure nothing else holds the single-instance mutex.
if ($Force) { Get-Process CampusPass -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Seconds 2 }

$p = Start-Process -FilePath $exe -ArgumentList '--background' -PassThru
Start-Sleep -Seconds 3
if ($p.HasExited) { Write-Host "$label exited immediately"; exit 1 }

$elapsed = 3
foreach ($mark in $atSeconds) {
    Start-Sleep -Seconds ($mark - $elapsed)
    $elapsed = $mark
    $s = Sample $p
    Write-Host ("{0,-14} t={1,3}s  Private {2,6} MB   Working {3,6} MB (peak {4,6} MB)   CPU {5,5} s   modules {6}" -f `
        $label, $mark, $s.PrivateMB, $s.WorkingMB, $s.PeakWorkMB, $s.CPUsec, $s.Modules)
}

# Trim the working set the way the OS would under pressure, to see the real floor.
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class Trim {
  [DllImport("psapi.dll")] public static extern bool EmptyWorkingSet(IntPtr h);
  [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint a,bool i,uint pid);
  public static void Do(int pid){ var h=OpenProcess(0x1F0FFF,false,(uint)pid); if(h!=IntPtr.Zero) EmptyWorkingSet(h); }
}
'@
[Trim]::Do($p.Id)
Start-Sleep -Seconds 2
$after = Sample $p
Write-Host ("{0,-14} after working-set trim:        Working {1,6} MB   Private {2,6} MB" -f `
    $label, $after.WorkingMB, $after.PrivateMB)

[Sig]::Stop()
Start-Sleep -Seconds 3
if (-not $p.HasExited) { Write-Host "$label did not stop cleanly"; $p.Kill() }
