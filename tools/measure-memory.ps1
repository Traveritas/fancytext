# ASCII-only. Measure WS + private bytes (commit) in hidden and visible states.
Add-Type -AssemblyName System.Windows.Forms

$p = Get-Process FancyText.Desktop -ErrorAction Stop
"pid: $($p.Id)"
"hidden : WS {0,7:N1} MB | commit {1,7:N1} MB" -f ($p.WorkingSet64/1MB), ($p.PrivateMemorySize64/1MB)

[System.Windows.Forms.SendKeys]::SendWait('%^{f}')
Start-Sleep -Milliseconds 1500
$p.Refresh()
"visible: WS {0,7:N1} MB | commit {1,7:N1} MB" -f ($p.WorkingSet64/1MB), ($p.PrivateMemorySize64/1MB)

# hide by switching focus away, wait for the 1.5s trim
[System.Windows.Forms.SendKeys]::SendWait('%{TAB}')
Start-Sleep -Seconds 4
$p.Refresh()
"hidden : WS {0,7:N1} MB | commit {1,7:N1} MB" -f ($p.WorkingSet64/1MB), ($p.PrivateMemorySize64/1MB)
