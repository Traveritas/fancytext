# ASCII-only: enumerate installed fonts and report which contain the problem combining marks.
Add-Type -AssemblyName PresentationCore

$targets = @(0x0313, 0x0314, 0x0330, 0x0333, 0x0347, 0x0353, 0x035E, 0x035F, 0x0336, 0x0305, 0x0488, 0x0F7C, 0x0E49, 0x1B44, 0xA9BF)
$names = @{ 0x0313='comma-above'; 0x0314='rev-comma-above'; 0x0330='tilde-below'; 0x0333='double-low-line'; 0x0347='equals-below'; 0x0353='x-below'; 0x035E='dbl-macron-above'; 0x035F='dbl-macron-below'; 0x0336='long-stroke'; 0x0305='overline'; 0x0488='comb-ring'; 0x0F7C='tibetan-o'; 0x0E49='thai-mai-tho'; 0x1B44='balinese'; 0xA9BF='javanese' }

$hits = @{}
foreach ($t in $targets) { $hits[$t] = @() }

$fontFiles = Get-ChildItem 'C:\Windows\Fonts' -Include *.ttf,*.ttc,*.otf -Recurse -ErrorAction SilentlyContinue
$tested = 0
foreach ($f in $fontFiles) {
    try {
        $gt = New-Object System.Windows.Media.GlyphTypeface($f.FullName)
        $cmap = $gt.CharacterToGlyphMap
        $tested++
        $fname = $null
        foreach ($k in $gt.FamilyNames.Keys) { if ($k.IetfLanguageTag -eq 'en-us') { $fname = $gt.FamilyNames[$k] } }
        if (-not $fname) { $fname = $f.Name }
        foreach ($t in $targets) {
            if ($cmap.ContainsKey($t) -and $hits[$t].Count -lt 8 -and $hits[$t] -notcontains $fname) {
                $hits[$t] += $fname
            }
        }
    } catch { }
}

"tested $tested font faces"
foreach ($t in $targets) {
    "U+{0:X4} {1,-22}: {2}" -f $t, $names[$t], ($(if ($hits[$t].Count) { $hits[$t] -join ', ' } else { '<<NO INSTALLED FONT>>' }))
}
