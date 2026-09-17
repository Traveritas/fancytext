namespace FancyText.Core;

/// <summary>
/// 样式家族聚族（家族目录钻取）：把 kebab ID 首段相同的大族折叠成列表里的一行族条目。
/// 收录规则：内置样式按 ID 首段（第一个 "-" 之前，无 "-" 取全名）归纳——规格指定的族全量进表，
/// 即使当前成员不足 <see cref="MinMembersToGroup"/>（underline/overline/smoke/zalgo，归族但不折叠，
/// 扩充到阈值后自动开始折叠）；其余首段须实测成员 ≥ 阈值才收录（enclose 5 / border 4 / bold 4 / sans 4，
/// 由 StyleCatalog*.cs 的 Id 表统计得出）。包样式不按 ID 归类，一律按包聚族（键 = "pack:" + 包名）。
/// </summary>
public static class StyleFamilies
{
    /// <summary>折叠阈值：当前筛选下同族可见成员达到该数才折叠成族条目。</summary>
    public const int MinMembersToGroup = 4;

    /// <summary>包样式的族键前缀：族键 = 前缀 + 包名（显示名取回包名原文）。</summary>
    public const string PackPrefix = "pack:";

    // (键 = kebab 首段, 中文名, 英文名)；添加新前缀前先在 StyleCatalog*.cs 实测成员数，不足阈值不进表
    private static readonly (string Key, string Zh, string En)[] Table =
    [
        ("wing", "翅膀", "Wings"),
        ("juhua", "菊花体", "Chrysanthemum"),
        ("strikethrough", "删除线", "Strikethrough"),
        ("underline", "下划线", "Underline"),
        ("overline", "上划线", "Overline"),
        ("smoke", "冒烟", "Smoke"),
        ("zalgo", "魔鬼文字", "Zalgo"),
        ("enclose", "包围字", "Enclosed"),
        ("border", "边框", "Borders"),
        ("bold", "粗体系", "Bold"),
        ("sans", "无衬线系", "Sans"),
    ];

    /// <summary>
    /// 取样式的族键：包样式 → "pack:" + 包名；内置样式 → ID 首段命中聚族表返回该首段，否则 null（不参与折叠）。
    /// </summary>
    public static string? GetFamilyKey(TextStyle style)
    {
        if (style.Source is StyleSource.Pack { PackName: var packName })
        {
            return PackPrefix + packName;
        }

        var segment = FirstSegment(style.Id);
        return Table.Any(t => t.Key == segment) ? segment : null;
    }

    /// <summary>族显示名：内置族按语言取中/英；包族返回包名原文（包名本身不分语言）；未知键原样返回兜底。</summary>
    public static string FamilyDisplayName(string key, AppLanguage lang)
    {
        if (key.StartsWith(PackPrefix, StringComparison.Ordinal))
        {
            return key[PackPrefix.Length..];
        }

        foreach (var (k, zh, en) in Table)
        {
            if (k == key)
            {
                return lang == AppLanguage.English ? en : zh;
            }
        }

        return key;
    }

    /// <summary>kebab 首段：第一个 "-" 之前；无 "-" 的 ID（如 strikethrough/bold）取全名。</summary>
    private static string FirstSegment(string id)
    {
        var dash = id.IndexOf('-');
        return dash < 0 ? id : id[..dash];
    }
}
