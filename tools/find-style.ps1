# ASCII-only. New filter UI: summon popup, pick a category from the chip's dropdown
# Popup (separate top-level window, UIA ClassName 'Popup'), then walk down until a
# visible row whose text contains the needle; leave that row visible.
# NOTE (family folding): since v1.3.0, style families with >= 4 members collapse
# into a single family row ("name · N styles >") in All/category views. A needle
# targeting a style inside a collapsed family will NOT be visible from the top
# level; drill into the family row first (select it and send {ENTER}), or pick a
# needle in a flat (non-folded) style. Family rows dump with "N styles >" in meta.
# Usage: find-style.ps1 -NeedleChars <int[]> [-CatChars <int[]>]
#   NeedleChars: needle as UTF-16 code units (required)
#   CatChars:    category label as UTF-16 code units; default = 0x7279 0x6548 (CJK effects)
# UIA facts (measured): main window and the dropdown Popup are separate top-level
# HWNDs (UIA ClassName 'Window' vs 'Popup'); the Popup is NOT listed under
# RootElement.Children, so top-level windows are found with EnumWindows + FromHandle.
# WPF ToggleButton shows up as ControlType.Button with a Toggle pattern only (no
# Invoke). The chip opens/closes via Toggle (its handlers are Checked/Unchecked),
# but menu items / fav / recent buttons use Click handlers, and TogglePattern.Toggle
# flips IsChecked WITHOUT raising Click (measured) - those must be REAL mouse clicks.
# The chip is the Button whose Name ends with U+25BE; menu item Buttons have an
# empty Name, the visible label lives in a child ControlType.Text element.
param([int[]]$NeedleChars, [int[]]$CatChars)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

if (-not $NeedleChars) { throw 'NeedleChars required' }
if (-not $CatChars) { $CatChars = @(0x7279, 0x6548) }  # category label code units
$needle = -join ($NeedleChars | ForEach-Object { [char]$_ })
$catName = -join ($CatChars | ForEach-Object { [char]$_ })
$procId = (Get-Process FancyText.Desktop -ErrorAction Stop).Id

Add-Type @'
using System;
using System.Runtime.InteropServices;
public class W7 {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
  public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  public struct POINT { public int X, Y; }
}
'@

function Click-Element($el) {
    # real left click at the element's clickable point (Toggle pattern does not raise Click)
    $x = 0; $y = 0
    try {
        $pt = $el.GetClickablePoint()
        $x = [int]$pt.X; $y = [int]$pt.Y
    } catch {
        $r = $el.Current.BoundingRectangle
        $x = [int]($r.X + $r.Width / 2); $y = [int]($r.Y + $r.Height / 2)
    }
    $old = New-Object W7+POINT
    [W7]::GetCursorPos([ref]$old) | Out-Null
    [W7]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 80
    [W7]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)  # LEFTDOWN
    Start-Sleep -Milliseconds 40
    [W7]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)  # LEFTUP
    Start-Sleep -Milliseconds 120
    [W7]::SetCursorPos($old.X, $old.Y) | Out-Null      # restore cursor
}

function Get-AppWindows {
    # $script:visWins must live at script scope: the EnumWindows callback cannot see function locals
    $script:visWins = New-Object System.Collections.ArrayList
    [W7]::EnumWindows({ param($h, $l)
        $owner = 0
        [W7]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
        if ($owner -eq $script:procId -and [W7]::IsWindowVisible($h)) { [void]$script:visWins.Add($h) }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    $found = @{ Main = $null; Popup = $null }
    foreach ($h in $script:visWins) {
        try {
            $el = [System.Windows.Automation.AutomationElement]::FromHandle($h)
            if ($el.Current.ClassName -eq 'Popup') { $found.Popup = $el }
            elseif ($el.Current.ClassName -eq 'Window') { $found.Main = $el }
        } catch { }
    }
    return $found
}

# summon; everything below must stay in this same powershell session (window hides on deactivate)
[System.Windows.Forms.SendKeys]::SendWait('%^{f}')

# poll for the main window (post-trim summon is slow); allow >=1.5s settle before UIA work
$main = $null
$sw = [System.Diagnostics.Stopwatch]::StartNew()
while ($sw.ElapsedMilliseconds -lt 6000) {
    $w = Get-AppWindows
    if ($w.Main) { $main = $w.Main; break }
    Start-Sleep -Milliseconds 150
}
if (-not $main) { throw 'no visible window after summon' }
$wait = 1500 - [int]$sw.ElapsedMilliseconds
if ($wait -gt 0) { Start-Sleep -Milliseconds $wait }

# filter chip = the Button whose Name ends with U+25BE (label like '<filter> v')
$chip = $null
foreach ($b in $main.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)))) {
    $n = $b.Current.Name
    if ($n -and $n.Length -gt 0 -and $n[$n.Length - 1] -eq [char]0x25BE) { $chip = $b; break }
}
if (-not $chip) { throw 'filter chip not found' }
$chipToggle = $chip.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)

# deterministic start: if a stale popup is open (chip checked), close it first
if ($chipToggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) {
    $chipToggle.Toggle()
    Start-Sleep -Milliseconds 400
}

# open the dropdown
$chipToggle.Toggle()
$proot = $null
$sw2 = [System.Diagnostics.Stopwatch]::StartNew()
while ($sw2.ElapsedMilliseconds -lt 2000) {
    $proot = (Get-AppWindows).Popup
    if ($proot) { break }
    Start-Sleep -Milliseconds 100
}
if (-not $proot) { throw 'filter popup did not open' }

# category item: menu Buttons have empty Name; match the child Text (label), real-click its parent Button
$tb = $proot.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Text)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $catName)))))
if (-not $tb) {
    'available category labels (as UTF-16 hex):'
    foreach ($t in $proot.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Text)))) {
        '  ' + (($t.Current.Name.ToCharArray() | ForEach-Object { '{0:X4}' -f [int]$_ }) -join ' ')
    }
    throw "category '$catName' not found in popup"
}
$item = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($tb)
Click-Element $item
Start-Sleep -Milliseconds 600  # popup closes, list rebuilds synchronously in the Click handler

function Get-VisibleMetas {
    $list = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'ListBox')))
    $items = $list.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'ListBoxItem')))
    $metas = @()
    foreach ($it in $items) {
        $tbs = $it.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'Preview')))
        foreach ($tb2 in $tbs) { $metas += $tb2.Current.Name }
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
