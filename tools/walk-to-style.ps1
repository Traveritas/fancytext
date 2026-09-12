# ASCII-only. Click the CJK-effect filter chip, walk selection down with arrows.
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

$chipName = -join @([char]0x4E2D, [char]0x6587, [char]0x7279, [char]0x6548)  # CJK effect chip label
$procId = (Get-Process FancyText.Desktop -ErrorAction Stop).Id

Add-Type @'
using System;
using System.Runtime.InteropServices;
public class W6 {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
  public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
'@
[System.Windows.Forms.SendKeys]::SendWait('%^{f}')
Start-Sleep -Milliseconds 900
$hwnd = [IntPtr]::Zero
[W6]::EnumWindows({ param($h, $l)
    $owner = 0
    [W6]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
    if ($owner -eq $script:procId -and [W6]::IsWindowVisible($h)) { $script:hwnd = $h; return $false }
    return $true
}, [IntPtr]::Zero) | Out-Null
if ($hwnd -eq [IntPtr]::Zero) { throw 'no visible window' }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# click filter chip
$chip = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $chipName)))
if (-not $chip) { throw 'chip not found' }
$chip.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 700

# walk selection down N steps (selection auto-scrolls the list)
$steps = 12
if ($args.Length -gt 0) { $steps = [int]$args[0] }
1..$steps | ForEach-Object { [System.Windows.Forms.SendKeys]::SendWait('{DOWN}'); Start-Sleep -Milliseconds 60 }
Start-Sleep -Milliseconds 500

# dump visible rows
$list = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'ListBox')))
$items = $list.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'ListBoxItem')))
foreach ($it in $items) {
    $tb = $it.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'TextBlock')))
    if ($tb) { "row: $($tb.Current.Name)" }
}
