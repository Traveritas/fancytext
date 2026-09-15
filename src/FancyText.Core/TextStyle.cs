namespace FancyText.Core;

/// <summary>样式分类，决定列表中的分组与顺序。</summary>
public enum TextStyleCategory
{
    /// <summary>特效（组合附加符号，中英通吃）：菊花体、删除线、花藤体、魔鬼文字等。</summary>
    CjkEffect,

    /// <summary>英文/字母花体（Unicode 区段查表映射，仅拉丁字母与数字）：粗体、花体、哥特体、泡泡字等。</summary>
    LatinFancy,

    /// <summary>装饰模板（前后缀/逐字包围）：翅膀、边框、括号。</summary>
    Decoration,

    /// <summary>拼写/方向变换：倒转、镜像、倒序、Leet。</summary>
    Transform,

    /// <summary>中文专属：火星文。</summary>
    Chinese,

    /// <summary>编码类：Base64、ROT13、摩斯电码。</summary>
    Encoding,
}

public static class TextStyleCategoryExtensions
{
    public static string DisplayName(this TextStyleCategory category) => category switch
    {
        TextStyleCategory.CjkEffect => "特效",
        TextStyleCategory.LatinFancy => "英文/字母花体",
        TextStyleCategory.Decoration => "装饰",
        TextStyleCategory.Transform => "变换",
        TextStyleCategory.Chinese => "中文",
        TextStyleCategory.Encoding => "编码",
        _ => category.ToString(),
    };
}

/// <summary>一个可用的文字样式 = 元数据 + 转换函数。</summary>
public sealed record TextStyle
{
    /// <summary>稳定 ID（kebab-case），用于测试与将来的收藏/排序持久化。</summary>
    public required string Id { get; init; }

    /// <summary>显示名（中文优先，可带英文别名）。</summary>
    public required string Name { get; init; }

    public required TextStyleCategory Category { get; init; }

    /// <summary>纯函数：输入原文，输出转换结果。不得抛异常（内部自行兜底）。</summary>
    public required Func<string, string> Transform { get; init; }

    /// <summary>机制说明（码点/来源），显示在详情页。</summary>
    public string? Note { get; init; }

    /// <summary>本样式的声明式定义（可序列化）。由 <see cref="StyleFactory.FromDefinition"/> 填充；Legacy 迁移副本为 null。</summary>
    public StyleDefinition? Definition { get; init; }

    /// <summary>来源：内置或外部样式包。由 <see cref="StyleFactory.FromDefinition"/> 填充；null 视为内置。</summary>
    public StyleSource? Source { get; init; }

    public string CategoryDisplayName => Category.DisplayName();
}
