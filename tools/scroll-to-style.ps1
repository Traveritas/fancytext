# ASCII-only. Scroll the popup list until target style rows are visible, then report row names.
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationClientSideProviders
Add-Type -AssemblyName System.Windows.Forms

$needle = -join @([char]0x9876, [char]0x7BAD, [char]0x5934, [char]0x5B57)  # target style label
$procId = (Get-Process FancyText.Desktop -ErrorAction Stop).Id

Add-Type @'
using System;
using System.Runtime.InteropServices;
public class W5 {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
  public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
'@

# 先唤出再枚举（弹窗失焦会自动隐藏）
[System.Windows.Forms.SendKeys]::SendWait('%^{f}')
Start-Sleep -Milliseconds 900

$hwnd = [IntPtr]::Zero
[W5]::EnumWindows({ param($h, $l)
    $owner = 0
    [W5]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
    if ($owner -eq $script:procId -and [W5]::IsWindowVisible($h)) { $script:hwnd = $h; return $false }
    return $true
}, [IntPtr]::Zero) | Out-Null
if ($hwnd -eq [IntPtr]::Zero) { throw 'no visible window' }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

$list = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'ListBox')))
if (-not $list) { throw 'no list' }

function Dump-Rows {
    $items = $list.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'ListBoxItem')))
    $rows = @()
    foreach ($it in $items) {
        $tb = $it.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'Preview')))
        if ($tb) { $rows += $tb.Current.Name }
    }
    return ,$rows
}

$scroll = $null
try { $scroll = $list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern) } catch {}

for ($i = 0; $i -lt 40; $i++) {
    $rows = Dump-Rows
    $hit = $rows | Where-Object { $_ -match $needle }
    if ($hit) {
        "FOUND at scroll step $i"
        $rows | ForEach-Object { "row: $_" }
        exit 0
    }
    if ($scroll -and -not $scroll.Current.VerticallyScrollable) { break }
    if ($scroll) {
        $before = $scroll.Current.VerticalScrollPercent
        $scroll.Scroll([System.Windows.Automation.ScrollAmount]::SmallIncrement,
                       [System.Windows.Automation.ScrollAmount]::NoAmount)
        Start-Sleep -Milliseconds 150
        if ([Math]::Abs($scroll.Current.VerticalScrollPercent - $before) -lt 0.01 -and $scroll.Current.VerticalScrollPercent -gt 99) { break }
    } else { break }
}
"target not found; last rows:"
Dump-Rows | ForEach-Object { "row: $_" }
