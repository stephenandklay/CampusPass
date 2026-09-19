param([Parameter(Mandatory = $true)][string]$exe,
     [Parameter(Mandatory = $true)][string]$out,
     [int]$waitMs = 4000)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Win {
  // Without this the whole script runs DPI-virtualised: GetWindowRect would hand
  // back 96-DPI coordinates while the screen grab is in physical pixels, so every
  // crop and MoveWindow below lands in the wrong place.
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
  public static void MakeDpiAware() { SetProcessDpiAwarenessContext(new IntPtr(-4)); }

  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int d, bool r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  public static void Click(int x, int y) {
    SetCursorPos(x, y);
    System.Threading.Thread.Sleep(120);
    mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
    mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
  }
}
'@

[Win]::MakeDpiAware()

$null = Start-Process -FilePath $exe
Start-Sleep -Milliseconds $waitMs

$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 20; $i++) {
    $c = Get-Process CampusPass -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
    if ($c) { $hwnd = $c.MainWindowHandle; break }
    Start-Sleep -Milliseconds 500
}
if ($hwnd -eq [IntPtr]::Zero) { Write-Host "NO WINDOW"; exit 1 }

# Park the window in the top-left, then raise it by clicking its own title bar.
# A real click is the only thing that reliably beats focus-stealing prevention.
# One set of numbers: MoveWindow and SetWindowPos disagreeing silently undid the
# placement here once already.
$left = 20; $top = 8; $width = 1225; $height = 850
[Win]::MoveWindow($hwnd, $left, $top, $width, $height, $true) | Out-Null
Start-Sleep -Milliseconds 400
[Win]::Click($left + 140, $top + 44)   # title bar, clear of any other window
Start-Sleep -Milliseconds 400
# A maximised/fullscreen foreground app can still beat a plain SetForegroundWindow,
# so pin topmost for the duration of the grab and release it afterwards.
[Win]::SetWindowPos($hwnd, [Win]::HWND_TOPMOST, $left, $top, $width, $height, 0x0040) | Out-Null
[Win]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 600

$r = New-Object Win+RECT
if (-not [Win]::GetWindowRect($hwnd, [ref]$r)) { Write-Host "GetWindowRect failed"; exit 1 }
$w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
[Win]::SetWindowPos($hwnd, [IntPtr]::(-2), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040) | Out-Null
Write-Host ("saved {0} ({1}x{2})" -f $out, $w, $h)
