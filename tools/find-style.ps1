# ASCII-only. Filter CJK category, walk down until a row whose meta line contains the needle, leave it visible.
param([int[]]$NeedleChars)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

$needle = -join ($NeedleChars | ForEach-Object { [char]$_ })
$chipName = -join @([char]0x4E2D, [char]0x6587, [char]0x7279, [char]0x6548)  # CJK effect chip
$procId = (Get-Process FancyText.Desktop -ErrorAction Stop).Id

Add-Type @'
using System;
using System.Runtime.InteropServices;
public class W7 {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
  public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
'@
[System.Windows.Forms.SendKeys]::SendWait('%^{f}')
Start-Sleep -Milliseconds 900
$hwnd = [IntPtr]::Zero
[W7]::EnumWindows({ param($h, $l)
    $owner = 0
    [W7]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
    if ($owner -eq $script:procId -and [W7]::IsWindowVisible($h)) { $script:hwnd = $h; return $false }
    return $true
}, [IntPtr]::Zero) | Out-Null
if ($hwnd -eq [IntPtr]::Zero) { throw 'no visible window' }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

$chip = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $chipName)))
if (-not $chip) { throw 'chip not found' }
$chip.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 700

function Get-VisibleMetas {
    $list = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'ListBox')))
    $items = $list.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'ListBoxItem')))
    $metas = @()
    foreach ($it in $items) {
        $tbs = $it.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'TextBlock')))
        foreach ($tb in $tbs) { $metas += $tb.Current.Name }
    }
    return ,$metas
}

for ($i = 0; $i -lt 90; $i++) {
    $metas = Get-VisibleMetas
    if ($metas | Where-Object { $_ -match [regex]::Escape($needle) }) {
        "FOUND '$needle' at step $i"
        $metas | Select-Object -Last 16 | ForEach-Object { "meta: $_" }
        exit 0
    }
    [System.Windows.Forms.SendKeys]::SendWait('{DOWN}')
    Start-Sleep -Milliseconds 70
}
"NOT FOUND"
$metas | Select-Object -Last 8 | ForEach-Object { "meta: $_" }
exit 1
