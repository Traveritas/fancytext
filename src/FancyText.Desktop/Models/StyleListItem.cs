namespace FancyText.Desktop.Models;

/// <summary>
/// 列表项绑定记录类：转换预览 + 样式元数据 + 收藏态。
/// 列表随输入/筛选/收藏变化整体重建（126 个量级无需增量），故不需要 INotifyPropertyChanged。
/// </summary>
internal sealed class StyleListItem
{
    /// <summary>稳定样式 ID，回车复制全文时据此取回完整 TextStyle。</summary>
    public required string StyleId { get; init; }

    public required string Name { get; init; }

    /// <summary>单行化后的预览文本（仅对前 64 个字素做过转换）。</summary>
    public required string PreviewText { get; init; }

    /// <summary>是否已收藏（⭐ 标识）。</summary>
    public required bool IsPinned { get; init; }

    /// <summary>分类中文名，独立成字段便于直接绑定。</summary>
    public required string CategoryName { get; init; }

    /// <summary>列表第二行小字：收藏加 ⭐ 前缀，后接样式名与分类。</summary>
    public string Meta => IsPinned ? $"⭐ {Name} · {CategoryName}" : $"{Name} · {CategoryName}";
}
