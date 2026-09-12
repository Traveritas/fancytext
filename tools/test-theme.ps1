# ASCII-only. Switch settings theme combo to dark, wake popup.
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

$darkName = -join @([char]0x6DF1, [char]0x8272)   # dark theme label
$lightName = -join @([char]0x6D45, [char]0x8272)  # light theme label

$proc = Get-Process FancyText.Desktop -ErrorAction Stop
$settings = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
if (-not $settings) { throw 'no settings window' }

$combos = $settings.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ComboBox)))
"combo count: $($combos.Count)"

$target = $darkName
if ($args.Length -gt 0 -and $args[0] -eq 'light') { $target = $lightName }
$selected = $false
foreach ($c in $combos) {
    try {
        $exp = $c.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        $exp.Expand()
        Start-Sleep -Milliseconds 250
        $item = $c.FindFirst([System.Windows.Automation.TreeScope]::Subtree,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::NameProperty, $target)))
        $exp.Collapse()
        if ($item) {
            $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            $selected = $true
            "selected: $target"
            break
        }
    } catch { "combo error: $($_.Exception.Message)" }
}
if (-not $selected) { throw "target item not found: $target" }

Start-Sleep -Milliseconds 800
[System.Windows.Forms.SendKeys]::SendWait('%^{f}')
Start-Sleep -Milliseconds 1200
"popup wake sent"
