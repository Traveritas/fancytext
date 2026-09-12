# ASCII-only. Atomic: find hwnd via EnumWindows -> UIA set input -> dump preview rows.
Add-Type -AssemblyName UIAutomationClient

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public class W2 {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
  public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
'@

$procId = (Get-Process FancyText.Desktop -ErrorAction Stop).Id
$hwnd = [IntPtr]::Zero
[W2]::EnumWindows({ param($h, $l)
    $owner = 0
    [W2]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
    if ($owner -eq $procId -and [W2]::IsWindowVisible($h)) { $script:hwnd = $h; return $false }
    return $true
}, [IntPtr]::Zero) | Out-Null
if ($hwnd -eq [IntPtr]::Zero) { throw 'no visible window' }
"hwnd = $hwnd"

$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'TextBox')
$edit = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
if (-not $edit) { throw 'no TextBox' }
$vp = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$vp.SetValue([char]0x597D + [char]0x7684 + 'ok')
Start-Sleep -Milliseconds 900

$rect = $root.Current.BoundingRectangle
"window: {0:N0},{1:N0} {2:N0}x{3:N0}" -f $rect.X, $rect.Y, $rect.Width, $rect.Height

$list = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'ListBoxItem')))
"items: $($list.Count)"
$i = 0
foreach ($it in $list) {
    if ($i -ge 14) { break }
    $tb = $it.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'TextBlock')))
    $t = if ($tb) { $tb.Current.Name } else { '<no text>' }
    $hex = ($t.ToCharArray() | Select-Object -First 20 | ForEach-Object { '{0:X4}' -f [int]$_ }) -join ' '
    "row {0,2}: {1}  [{2}]" -f $i, $t, $hex
    $i++
}
