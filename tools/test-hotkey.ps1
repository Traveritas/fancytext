# ASCII-only. Hotkey recorder E2E: click pill -> send combo -> read back -> revert.
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

$proc = Get-Process FancyText.Desktop -ErrorAction Stop
$settings = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)

function Find-Button($root, [string]$name) {
    $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)))
}

function Record([string]$keys, [string]$expectLabel) {
    $pill = Find-Button $settings $expectLabel
    if (-not $pill) { throw "pill not found: $expectLabel" }
    $pill.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds 700
    $after = Find-Button $settings $expectLabel
    if ($after) { "FAIL: pill still shows $expectLabel" }
    $anyPill = $settings.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)))
    "after $keys -> pill now: '$($anyPill.Current.Name)'"
}

# rebind to Ctrl+Alt+G, then back to Ctrl+Alt+F
Record '^%g' 'Ctrl+Alt+F'
Record '^%f' 'Ctrl+Alt+G'
