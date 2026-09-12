# ASCII-only. Collect every codepoint the CLI catalog actually emits, classify coverage.
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$out = dotnet run --project src/FancyText.Cli -- list 2>$null
$chars = @{}
foreach ($line in $out) {
    foreach ($ch in $line.ToCharArray()) {
        $cp = [int]$ch
        if ($cp -lt 128) { continue }
        if ($chars.ContainsKey($cp)) { $chars[$cp]++ } else { $chars[$cp] = 1 }
    }
}

# coverage buckets (current run-splitting + font chain)
$covered = { param($cp)
    ($cp -ge 0x0300 -and $cp -le 0x036F) -or ($cp -ge 0x0483 -and $cp -le 0x0489) -or
    ($cp -ge 0x20D0 -and $cp -le 0x20FF) -or ($cp -ge 0xA670 -and $cp -le 0xA672) }
$inheritedCandidate = { param($cp)
    ($cp -ge 0x1AB0 -and $cp -le 0x1AFF) -or ($cp -ge 0x1DC0 -and $cp -le 0x1DFF) -or
    ($cp -ge 0xFE00 -and $cp -le 0xFE0F) -or ($cp -ge 0xFE20 -and $cp -le 0xFE2F) -or
    ($cp -ge 0x0591 -and $cp -le 0x05C7) -or ($cp -ge 0x064B -and $cp -le 0x0655) -or
    ($cp -ge 0x0E31 -and $cp -le 0x0E3A) -or ($cp -ge 0x302A -and $cp -le 0x302F) -or
    ($cp -ge 0xA8E0 -and $cp -le 0xA8F1) -or ($cp -ge 0x1D165 -and $cp -le 0x1D169) }

$byBucket = @{}
foreach ($cp in ($chars.Keys | Sort-Object)) {
    $bucket = if (& $covered $cp) { 'COVERED-runit' }
              elseif ($cp -ge 0x4E00 -and $cp -le 0x9FFF) { 'CJK' }
              elseif (& $inheritedCandidate $cp) { 'INHERITED-candidate' }
              else { 'OTHER-symbol' }
    if (-not $byBucket.ContainsKey($bucket)) { $byBucket[$bucket] = @() }
    $byBucket[$bucket] += $cp
}

foreach ($k in $byBucket.Keys) {
    "=== $k ($($byBucket[$k].Count) codepoints) ==="
    $byBucket[$k] | ForEach-Object { "U+{0:X4} x{1}" -f $_, $chars[$_] } | Select-Object -First 60
}
