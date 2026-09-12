# ASCII-only. Inject text into the visible popup via EnumWindows-found hwnd.
Add-Type -AssemblyName UIAutomationClient

Add-Type @'
using System;
using System.Runtime.InteropServices;
public class W4 {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
  public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
'@

Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait('%^{f}')
Start-Sleep -Milliseconds 900

$procId = (Get-Process FancyText.Desktop -ErrorAction Stop).Id
$hwnd = [IntPtr]::Zero
[W4]::EnumWindows({ param($h, $l)
    $owner = 0
    [W4]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
    if ($owner -eq $procId -and [W4]::IsWindowVisible($h)) { $script:hwnd = $h; return $false }
    return $true
}, [IntPtr]::Zero) | Out-Null
if ($hwnd -eq [IntPtr]::Zero) { throw 'no visible window' }

$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'TextBox')
$edit = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
if (-not $edit) { throw 'no TextBox' }
$vp = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$vp.SetValue([char]0x597D + [char]0x7684)
Start-Sleep -Milliseconds 1000
"input set, hwnd=$hwnd"
