# ASCII-only. End-to-end verification of the input MaxLines fix:
#   A) 300-line clipboard prefill -> edit capped ~4 lines, list stays operable, Ctrl+R+Enter copies FULL text
#   B) short prefill -> edit back to a single line (no regression)
#   C) 4500-char single-line prefill -> wrap keeps the same cap
# Requires: desktop.json with prefillClipboard=true, prefillSelectionPlus=false (compat mode off),
# and the default Ctrl+Alt+F hotkey. Summon MUST go through the hotkey (second-instance summon
# leaves the popup visible but WITHOUT foreground: keystrokes get lost, window never hides).
# Usage: powershell -NoProfile -File verify-longtext.ps1 -ExePath <path-to-FancyText.Desktop.exe>
param([Parameter(Mandatory = $true)][string]$ExePath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

Add-Type @'
using System;
using System.Runtime.InteropServices;
public class WL {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
  public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
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
  [WL]::EnumWindows({ param($h, $l)
    $owner = 0
    [WL]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
    if ($owner -eq $procId -and [WL]::IsWindowVisible($h)) { $script:hwnd = $h; return $false }
    return $true
  }, [IntPtr]::Zero) | Out-Null
  return $script:hwnd
}

function Find-One($root, [string]$class) {
  return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::ClassNameProperty, $class)))
}

# Summon via hotkey so the popup actually holds foreground; poll + settle (post-trim summon is slow).
function Summon-Hotkey([int]$procId) {
  [System.Windows.Forms.SendKeys]::SendWait('%^{f}')
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $hwnd = [IntPtr]::Zero
  while ($sw.ElapsedMilliseconds -lt 6000) {
    $hwnd = Find-PopupHwnd $procId
    if ($hwnd -ne [IntPtr]::Zero) { break }
    Start-Sleep -Milliseconds 150
  }
  if ($hwnd -eq [IntPtr]::Zero) { throw 'no visible popup after hotkey' }
  $remain = 1500 - [int]$sw.ElapsedMilliseconds
  if ($remain -gt 0) { Start-Sleep -Milliseconds $remain }
  if ([WL]::GetForegroundWindow() -ne $hwnd) {
    [WL]::SetForegroundWindow($hwnd) | Out-Null   # we qualify: this process synthesized the last input
    Start-Sleep -Milliseconds 200
  }
  if ([WL]::GetForegroundWindow() -ne $hwnd) { throw 'popup not foreground; keystrokes would be lost' }
  return $hwnd
}

function Summon-AndProbe([int]$procId, [string]$label) {
  $root = [System.Windows.Automation.AutomationElement]::FromHandle((Summon-Hotkey $procId))
  $edit = Find-One $root 'TextBox'
  $list = Find-One $root 'ListBox'
  if (-not $edit) { throw "$label : no TextBox" }
  if (-not $list) { throw "$label : no ListBox" }
  $er = $edit.Current.BoundingRectangle
  $lr = $list.Current.BoundingRectangle
  $wr = $root.Current.BoundingRectangle
  $val = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
  $lines = @($val -split "`n").Count
  $items = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'ListBoxItem'))).Count
  $sb = $edit.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'ScrollBar'))).Count
  $msg = "{0}: window={1:N0}x{2:N0} editH={3:N1} listH={4:N1} items={5} value={6}ch/{7}ln sb={8}" -f `
    $label, $wr.Width, $wr.Height, $er.Height, $lr.Height, $items, $val.Length, $lines, $sb.Count
  $msg
  return @{ Root = $root; EditH = $er.Height; ListH = $lr.Height; Items = $items; Lines = $lines; Len = $val.Length; Sb = $sb.Count }
}

$script:fail = 0
function Check([bool]$ok, [string]$what) {
  if ($ok) { "PASS  $what" } else { $script:fail++; "FAIL  $what" }
}

Start-Process -FilePath $ExePath | Out-Null
Start-Sleep -Milliseconds 3000
$appId = Get-AppPid
try {
  # --- A: 300-line clipboard prefill ---
  $long = 1..300 | ForEach-Object { 'line {0:d3} the quick brown fox jumps' -f $_ }
  Set-Clipboard -Value ($long -join "`r`n")
  $a = Summon-AndProbe $appId 'A-long'
  Check ($a.EditH -gt 90 -and $a.EditH -lt 220) "A: edit capped to 4 lines (~118px), got $($a.EditH)"
  Check ($a.ListH -gt 150) "A: list still operable >150px, got $($a.ListH)"
  Check ($a.Items -ge 5) "A: list rows present, got $($a.Items)"
  Check ($a.Lines -ge 300) "A: full 300-line text seeded, got $($a.Lines) lines"
  Check ($a.Sb -ge 1) "A: vertical scrollbar present, got $($a.Sb)"

  # Ctrl+R picks a random real style row (never a family/back row), then Enter copies the FULL text.
  $before = Get-Clipboard -Raw
  [System.Windows.Forms.SendKeys]::SendWait('^r')
  Start-Sleep -Milliseconds 400
  [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
  Start-Sleep -Milliseconds 1500
  $after = Get-Clipboard -Raw
  $afterLines = @($after -split "`n").Count
  Check ($after.Length -gt 0 -and $after -ne $before) "A: Enter produced transformed clipboard ($($before.Length) -> $($after.Length) chars)"
  Check ($after.Length -gt 1000) "A: copied FULL text, not the 64-grapheme preview (got $($after.Length) chars, seed was $($before.Length))"
  "INFO  A: copied result spans $afterLines lines (seed $($a.Lines))"

  # --- B: short prefill (no regression) ---
  Set-Clipboard -Value 'ok'
  $b = Summon-AndProbe $appId 'B-short'
  Check ($b.EditH -lt 50) "B: short input stays single line <50px, got $($b.EditH)"
  Check ($b.Len -eq 2 -and $b.Lines -eq 1) "B: re-seeded 'ok' after hide, got $($b.Len) chars / $($b.Lines) lines"
  Check ($b.Items -ge 5) "B: list intact, got $($b.Items)"

  # Hide via Esc so the next summon takes the full prefill path (visible popup early-returns).
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
  Start-Sleep -Milliseconds 800

  # --- C: 4500-char single line (wrap case) ---
  Set-Clipboard -Value ('longword-' * 500)
  $c = Summon-AndProbe $appId 'C-single'
  Check ($c.Len -eq 4500) "C: seeded 4500 chars, got $($c.Len)"
  Check ($c.EditH -gt 90 -and $c.EditH -lt 220) "C: single-line wrap hits the same cap, got $($c.EditH)"
  Check ($c.Items -ge 5) "C: list intact, got $($c.Items)"
}
finally {
  Stop-Process -Name FancyText.Desktop -Force -ErrorAction SilentlyContinue
}

""
if ($script:fail -eq 0) { 'ALL PASS' } else { "$($script:fail) CHECK(S) FAILED"; exit 1 }
