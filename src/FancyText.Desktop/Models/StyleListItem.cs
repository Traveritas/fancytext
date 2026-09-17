namespace FancyText.Desktop.Models;

/// <summary>
/// 列表项绑定记录类：转换预览 + 样式元数据 + 收藏态。
/// 列表随输入/筛选/收藏变化整体重建（126 个量级无需增量），故不需要 INotifyPropertyChanged。
/// 家族目录钻取：Kind 区分样式行/族条目/返回行（族条目 Enter 钻入、返回行 Enter/Esc 钻出）。
/// </summary>
internal sealed class StyleListItem
{
    /// <summary>样式行（Enter 复制全文）。</summary>
    public const int KindStyle = 0;

    /// <summary>族条目（Enter 钻入该族；外观同普通行，靠 meta 的「n 个样式 ▸」提示）。</summary>
    public const int KindFamily = 1;

    /// <summary>钻取视图首行的返回行（Enter/Esc 钻出）。</summary>
    public const int KindBack = 2;

    /// <summary>稳定样式 ID，回车复制全文时据此取回完整 TextStyle。族条目存代表样式（族内第一个）的 ID；返回行为合成 ID。</summary>
    public required string StyleId { get; init; }

    public required string Name { get; init; }

    /// <summary>单行化后的预览文本（仅对前 64 个字素做过转换）。族条目存代表样式的转换结果；返回行为 ‹。</summary>
    public required string PreviewText { get; init; }

    /// <summary>是否已收藏（⭐ 标识）。</summary>
    public required bool IsPinned { get; init; }

    /// <summary>分类中文名，独立成字段便于直接绑定。</summary>
    public required string CategoryName { get; init; }

    /// <summary>行类型：<see cref="KindStyle"/>（默认）/ <see cref="KindFamily"/> / <see cref="KindBack"/>。</summary>
    public int Kind { get; init; }

    /// <summary>族键（StyleFamilies.GetFamilyKey）：样式行携带以便折叠/钻取归组；无族为 null。</summary>
    public string? FamilyKey { get; init; }

    /// <summary>族成员数（仅族条目用于 meta 计数）。</summary>
    public int MemberCount { get; init; }

    /// <summary>meta 文案覆盖：族条目（n 个样式 ▸）与返回行（空）使用；null 时按「⭐ 名称 · 分类」组合。</summary>
    public string? MetaOverride { get; init; }

    /// <summary>列表第二行小字：收藏加 ⭐ 前缀，后接样式名与分类；族条目/返回行用 <see cref="MetaOverride"/>。</summary>
    public string Meta => MetaOverride ?? (IsPinned ? $"⭐ {Name} · {CategoryName}" : $"{Name} · {CategoryName}");
}
