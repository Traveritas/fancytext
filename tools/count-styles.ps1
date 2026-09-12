# ASCII-only. Count StyleCatalog.All precisely by loading Core dll.
$core = Get-ChildItem -Recurse "src\FancyText.Core\bin" -Filter FancyText.Core.dll |
    Where-Object { $_.FullName -match 'Release' } | Select-Object -First 1
"dll: $($core.FullName)"
Add-Type -Path $core.FullName -PassThru | Out-Null
$t = [Type]::GetType("FancyText.Core.StyleCatalog, FancyText.Core")
if (-not $t) {
    # fall back: search loaded assemblies
    $t = [System.Reflection.Assembly]::LoadFrom($core.FullName).GetTypes() |
        Where-Object { $_.Name -eq 'StyleCatalog' } | Select-Object -First 1
}
$prop = $t.GetProperty('All', [System.Reflection.BindingFlags]'Public,Static')
$all = $prop.GetValue($null)
"catalog count: $($all.Count)"
$byCat = @{}
foreach ($s in $all) { $c = $s.Category.ToString(); $byCat[$c] = 1 + [int]$byCat[$c] }
$byCat.GetEnumerator() | Sort-Object Name | ForEach-Object { "{0}: {1}" -f $_.Key, $_.Value }
