# ASCII-only. Repro: double-clicking the drill-view Back row should return to the top
# level; reported bug: the window exits (hides) instead.
# Flow per run: start exe -> hotkey summon -> find first family row (meta ends U+25B8)
# -> walk selection there with {DOWN} -> {ENTER} to drill in -> assert Back row
# (preview U+2039) at top -> REAL mouse double-click on it -> classify outcome:
#   window hidden + clipboard changed => copy path (CommitSelected -> HideAfterCopy)
#   window hidden + clipboard same   => deactivate/hide path
#   window visible + family rows back => OK (no bug)
# Requires desktop.json with prefillClipboard=false, prefillSelection=false, default hotkey.
# Usage: powershell -NoProfile -File verify-backrow.ps1 -ExePath <FancyText.Desktop.exe>
param([Parameter(Mandatory = $true)][string]$ExePath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

Add-Type @'
using System;
using System.Runtime.InteropServices;
public class WB {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
  public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@

function Get-AppPid {
  $p = @(Get-Process FancyText.Desktop -ErrorAction Stop)
  if ($p.Count -ne 1) { throw "expected 1 FancyText.Desktop process, found $($p.Count)" }
  return $p[0].Id
}

function Find-PopupHwnd([int]$procId) {
  $script:hwnd = [IntPtr]::Zero
  [WB]::EnumWindows({ param($h, $l)
    $owner = 0
    [WB]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
    if ($owner -eq $procId -and [WB]::IsWindowVisible($h)) { $script:hwnd = $h; return $false }
    return $true
  }, [IntPtr]::Zero) | Out-Null
  return $script:hwnd
}

function Find-One($root, [string]$class) {
  return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::ClassNameProperty, $class)))
}

function Get-Rows($list) {
  return $list.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'ListBoxItem')))
}

function Row-Text($item, [string]$automationId) {
  $tb = $item.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)))
  if ($tb) { return $tb.Current.Name }
  return $null
}

function Real-DoubleClick($el) {
  $x = 0; $y = 0
  try {
    $pt = $el.GetClickablePoint()
    $x = [int]$pt.X; $y = [int]$pt.Y
  } catch {
    $r = $el.Current.BoundingRectangle
    $x = [int]($r.X + $r.Width / 2); $y = [int]($r.Y + $r.Height / 2)
  }
  [WB]::SetCursorPos($x, $y) | Out-Null
  Start-Sleep -Milliseconds 80
  [WB]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)   # LEFTDOWN
  [WB]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)   # LEFTUP
  Start-Sleep -Milliseconds 40
  [WB]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)   # LEFTDOWN (system merges into dblclk)
  [WB]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)   # LEFTUP
}

$clipBefore = Get-Clipboard -Raw

Start-Process -FilePath $ExePath | Out-Null
Start-Sleep -Milliseconds 3000
$appId = Get-AppPid
try {
  [System.Windows.Forms.SendKeys]::SendWait('%^{f}')
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $hwnd = [IntPtr]::Zero
  while ($sw.ElapsedMilliseconds -lt 6000) {
    $hwnd = Find-PopupHwnd $appId
    if ($hwnd -ne [IntPtr]::Zero) { break }
    Start-Sleep -Milliseconds 150
  }
  if ($hwnd -eq [IntPtr]::Zero) { throw 'no visible popup after hotkey' }
  $remain = 1500 - [int]$sw.ElapsedMilliseconds
  if ($remain -gt 0) { Start-Sleep -Milliseconds $remain }
  # foreground assert: keys are lost if the popup never took foreground (SafeActivate can lose the race)
  if ([WB]::GetForegroundWindow() -ne $hwnd) {
    [WB]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 200
  }
  if ([WB]::GetForegroundWindow() -ne $hwnd) { throw 'popup not foreground; keystrokes would be lost' }
  $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
  $list = Find-One $root 'ListBox'
  if (-not $list) { throw 'no ListBox' }

  # locate the first family row (meta text ends with U+25B8)
  $rows = Get-Rows $list
  $familyIdx = -1
  for ($i = 0; $i -lt $rows.Count; $i++) {
    $meta = Row-Text $rows[$i] 'Meta'
    if ($meta -and $meta.Length -gt 0 -and $meta[$meta.Length - 1] -eq [char]0x25B8) { $familyIdx = $i; break }
  }
  if ($familyIdx -lt 0) { throw 'no family row found at top level' }
  "family row at index $familyIdx"

  # selection starts at first item; walk down to the family row, then drill in.
  # NOTE: PS `1..0` expands to @(1,0) - use an explicit loop; and re-assert selection
  # before ENTER (a lost arrow key once landed on a style row and copied+hid instead).
  for ($i = 0; $i -lt $familyIdx; $i++) {
    [System.Windows.Forms.SendKeys]::SendWait('{DOWN}')
    Start-Sleep -Milliseconds 80
  }
  Start-Sleep -Milliseconds 300
  $sel = $list.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
  $selMeta = $null
  foreach ($s in $sel.Current.GetSelection()) { $selMeta = Row-Text $s 'Meta' }
  "selected before ENTER: $selMeta"
  if (-not ($selMeta -and $selMeta.Length -gt 0 -and $selMeta[$selMeta.Length - 1] -eq [char]0x25B8)) {
    throw "selection is not on the family row (got '$selMeta'); walk was flaky"
  }
  [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
  Start-Sleep -Milliseconds 800
  "after ENTER: hwnd=$(Find-PopupHwnd $appId) rows=$(@(Get-Rows $list).Count) clipChanged=$((Get-Clipboard -Raw) -ne $clipBefore)"

  # drill view: Back row (preview U+2039) must be the first row
  $rows = Get-Rows $list
  if ($rows.Count -lt 2) { throw 'drill view did not open (too few rows)' }
  $backPrev = Row-Text $rows[0] 'Preview'
  if ($backPrev -ne ([string][char]0x2039)) { throw "first row is not Back (preview='$backPrev')" }
  "drill view open: $($rows.Count) rows, Back row on top"

  Real-DoubleClick $rows[0]
  Start-Sleep -Milliseconds 1200

  # classify outcome
  $hwndAfter = Find-PopupHwnd $appId
  if ($hwndAfter -eq [IntPtr]::Zero) {
    $clipAfter = Get-Clipboard -Raw
    if ($clipAfter -ne $clipBefore) {
      $hex = ($clipAfter.ToCharArray() | Select-Object -First 16 | ForEach-Object { '{0:X4}' -f [int]$_ }) -join ' '
      "RESULT: EXIT via copy path (window hid, clipboard changed) -- BUG REPRODUCED"
      "clip head: $hex"
    } else {
      'RESULT: EXIT via hide/deactivate path (window hid, clipboard unchanged) -- BUG REPRODUCED'
    }
    exit 2
  }

  $rows = Get-Rows $list
  $hasBack = $false
  foreach ($r in $rows) {
    $p = Row-Text $r 'Preview'
    if ($p -eq ([string][char]0x2039)) { $hasBack = $true; break }
  }
  $famCount = 0
  foreach ($r in $rows) {
    $m = Row-Text $r 'Meta'
    if ($m -and $m.Length -gt 0 -and $m[$m.Length - 1] -eq [char]0x25B8) { $famCount++ }
  }
  if (-not $hasBack -and $famCount -ge 1) {
    "RESULT: OK -- back row returned to top level ($($rows.Count) rows, $famCount family rows)"
    exit 0
  }
  "RESULT: UNEXPECTED -- window visible, backRow=$hasBack familyRows=$famCount rows=$($rows.Count)"
  exit 3
}
finally {
  Stop-Process -Name FancyText.Desktop -Force -ErrorAction SilentlyContinue
}
