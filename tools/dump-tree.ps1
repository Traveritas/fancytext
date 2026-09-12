# ASCII-only. Full UIA subtree dump under the visible FancyText window.
Add-Type -AssemblyName UIAutomationClient

Add-Type @'
using System;
using System.Runtime.InteropServices;
public class W3 {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
  public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  public struct RECT { public int L, T, R, B; }
}
'@

$procs = Get-Process FancyText.Desktop -ErrorAction SilentlyContinue
"processes: " + (($procs | ForEach-Object { $_.Id }) -join ', ')

foreach ($proc in $procs) {
  $hwnd = [IntPtr]::Zero
  [W3]::EnumWindows({ param($h, $l)
      $owner = 0
      [W3]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
      if ($owner -eq $script:proc.Id -and [W3]::IsWindowVisible($h)) { $script:hwnd = $h; return $false }
      return $true
  }, [IntPtr]::Zero) | Out-Null
  if ($hwnd -eq [IntPtr]::Zero) { "pid $($proc.Id): no visible window"; continue }
  $r = New-Object W3+RECT
  [W3]::GetWindowRect($hwnd, [ref]$r) | Out-Null
  "pid $($proc.Id): hwnd=$hwnd rect=$($r.L),$($r.T)-$($r.R),$($r.B)"

  $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
  $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  $count = 0
  function Dump($el, [int]$depth) {
    if ($script:count -ge 60) { return }
    $script:count++
    $indent = '  ' * $depth
    $name = $el.Current.Name
    if ($name -and $name.Length -gt 48) { $name = $name.Substring(0, 48) }
    $line = "{0}{1} name='{2}'" -f $indent, $el.Current.ClassName, $name
    $line
    $child = $script:walker.GetFirstChild($el)
    while ($child) {
      Dump $child ($depth + 1)
      $child = $script:walker.GetNextSibling($child)
    }
  }
  Dump $root 0
}
