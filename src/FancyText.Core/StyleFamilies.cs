namespace FancyText.Core;

/// <summary>
/// 样式家族聚族（家族目录钻取）：把同类的一群样式折叠成列表里的一行族条目。
/// 内置样式按下方显式成员表归族（ID → 族），不再按 ID 首段推断——首段归族曾把 bold-script（花体）塞进
/// 「粗体系」、把云朵/蝴蝶结等边框塞进「翅膀」。ID 是收藏/最近的持久键，不能为了归族改名，所以归族与 ID 解耦。
/// 规格指定的族即使成员不足 <see cref="MinMembersToGroup"/> 也进表（underline/overline/smoke/zalgo：归族但不折叠，
/// 扩充到阈值后自动开始折叠）。包样式不查表，按包聚族（键 = "pack:" + 包名）；自定义类别的包样式例外——不聚族直接平铺（筛选分类已是它们的分组，族条目反而多一层钻取）。
/// </summary>
public static class StyleFamilies
{
    /// <summary>折叠阈值：当前筛选下同族可见成员达到该数才折叠成族条目。</summary>
    public const int MinMembersToGroup = 4;

    /// <summary>包样式的族键前缀：族键 = 前缀 + 包名（显示名取回包名原文）。</summary>
    public const string PackPrefix = "pack:";

    // (族键, 中文名, 英文名, 成员 ID)；新增内置样式时在这里显式归族，不登记即不参与折叠
    private static readonly (string Key, string Zh, string En, string[] Members)[] Table =
    [
        ("wing", "翅膀", "Wings",
        [
            "wing-classic", "wing-fancy", "wing-delicate", "wing-crown", "wing-flower", "wing-elegant", "wing-diamond",
            "wing-double", "wing-shimmer", "wing-soft", "wing-triangle", "wing-retro", "wing-angel",
        ]),
        ("tibetan", "藏式花纹", "Tibetan Ornaments",
        [
            "wing-mystic", "wing-tibetan", "wing-sanskrit", "wing-tibetan-ornament", "wing-charm", "wing-nested", "wing-moonlight",
        ]),
        ("border", "边框", "Borders",
        [
            "border-star", "border-aesthetic", "border-cute", "border-brackets-jp",
            "wing-cloud", "wing-bow", "wing-heart", "wing-bunny", "wing-asterism", "wing-dots", "wing-love", "wing-starmoon",
        ]),
        ("juhua", "菊花体", "Chrysanthemum", ["juhua-1", "juhua-2", "juhua-3", "juhua-4"]),
        ("strikethrough", "删除线", "Strikethrough",
            ["strikethrough", "strikethrough-double", "strikethrough-slash", "strikethrough-tilde"]),
        ("underline", "下划线", "Underline", ["underline", "underline-double", "underline-wavy"]),
        ("overline", "上划线", "Overline", ["overline", "overline-double"]),
        ("smoke", "冒烟", "Smoke", ["smoke", "smoke-thai", "smoke-arabic"]),
        ("zalgo", "魔鬼文字", "Zalgo", ["zalgo-mini", "zalgo-normal", "zalgo-max"]),
        ("enclose", "包围字", "Enclosed",
            ["enclose-circle", "enclose-square", "enclose-diamond", "enclose-forbidden", "enclose-triangle"]),
        ("sans", "无衬线系", "Sans", ["sans", "sans-bold", "sans-italic", "sans-bold-italic"]),
    ];

    private static readonly Dictionary<string, string> FamilyById = Table
        .SelectMany(t => t.Members.Select(id => (Id: id, t.Key)))
        .ToDictionary(x => x.Id, x => x.Key, StringComparer.Ordinal);

    /// <summary>聚族表全部族键，供一致性断言/将来其它端复用。</summary>
    public static IReadOnlyCollection<string> KnownKeys { get; } = Table.Select(t => t.Key).ToArray();

    /// <summary>族成员表（族键 → 成员 ID），供一致性断言。</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Members { get; } =
        Table.ToDictionary(t => t.Key, t => (IReadOnlyList<string>)t.Members, StringComparer.Ordinal);

    /// <summary>
    /// 取样式的族键：包样式 → 自定义类别不聚族（筛选分类已是它们的分组，再叠包族要多钻一层），
    /// 其余按 "pack:" + 包名；内置样式 → 查显式成员表，未登记返回 null（不参与折叠）。
    /// </summary>
    public static string? GetFamilyKey(TextStyle style)
    {
        if (style.Source is StyleSource.Pack { PackName: var packName })
        {
            return style.Category == TextStyleCategory.Custom ? null : PackPrefix + packName;
        }

        return FamilyById.GetValueOrDefault(style.Id);
    }

    /// <summary>族显示名：内置族按语言取中/英；包族返回包名原文（包名本身不分语言）；未知键原样返回兜底。</summary>
    public static string FamilyDisplayName(string key, AppLanguage lang)
    {
        if (key.StartsWith(PackPrefix, StringComparison.Ordinal))
        {
            return key[PackPrefix.Length..];
        }

        foreach (var (k, zh, en, _) in Table)
        {
            if (k == key)
            {
                return lang == AppLanguage.English ? en : zh;
            }
        }

        return key;
    }
}
