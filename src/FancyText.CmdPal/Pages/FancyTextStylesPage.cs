using FancyText.CmdPal.Commands;
using FancyText.CmdPal.Helpers;
using FancyText.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace FancyText.CmdPal.Pages;

/// <summary>
/// 二级页：某个分类（或全部）的样式列表。行数上限 = 分类内样式数。
/// 性能契约（继承自 v0.1.2 的修复）：
/// - 列表项/命令/详情持久创建，刷新只改属性——宿主侧 WinRT 包装对象恒定；
/// - 预览只转换输入的前 <see cref="MaxPreviewInputChars"/> 个字素（增量截断，与全文长度无关）；
/// - 回车复制时才对全文做转换（<see cref="CopyTextCommandEx"/> 惰性计算）。
/// 收藏：右键/更多命令收藏（⭐ 前缀标识），复制时记录"最近使用"。
/// </summary>
internal sealed partial class FancyTextStylesPage : DebouncedTextPageBase
{
    internal const string AllStylesPageId = "fancytext.styles.all";

    private const int MaxPreviewInputChars = 64;
    private const int MaxPreviewTitleChars = 120;

    private static readonly IconInfo PageIcon = new("\uE8C8");
    private static readonly IconInfo NoResultIcon = new("\uE7BA");
    private static readonly IReadOnlyDictionary<TextStyleCategory, Tag> CategoryTags =
        Enum.GetValues<TextStyleCategory>().ToDictionary(c => c, c => new Tag(c.DisplayName()));

    /// <summary>每个样式的一组持久 UI 对象（ListItem + 复制命令 + 详情），跨刷新复用、仅改属性。</summary>
    private sealed class StyleEntry
    {
        public required TextStyle Style { get; init; }
        public required ListItem Item { get; init; }
        public required CopyTextCommandEx Copy { get; init; }
        public required CopyTextCommandEx CopyKeepOpen { get; init; }
        public required TogglePinCommand TogglePin { get; init; }
        public required Details Details { get; init; }
    }

    private readonly UsageState _usage;
    private readonly List<StyleEntry> _entries;
    private readonly ListItem _noResultItem;

    /// <param name="category">null 表示"全部样式"。</param>
    public FancyTextStylesPage(SharedTextState shared, UsageState usage, TextStyleCategory? category)
        : base(shared, diagTag: category?.ToString() ?? "All", readSharedText: true)
    {
        _usage = usage;
        Category = category;

        var name = category is { } c ? c.DisplayName() : "全部样式";
        Id = PageIdFor(category);
        Icon = PageIcon;
        Name = name;
        Title = $"花式文字 · {name}";
        PlaceholderText = "输入或粘贴要转换的文字（留空则使用剪贴板）";
        ShowDetails = true;
        EmptyContent = new ListItem
        {
            Title = "输入文字以查看全部样式",
            Subtitle = "在搜索框输入，或复制文字后直接打开本页",
            Icon = PageIcon,
        };

        var styles = category is { } cat
            ? StyleCatalog.All.Where(s => s.Category == cat)
            : StyleCatalog.All;

        _entries = [];
        foreach (var style in styles)
        {
            void RecordUse() => _usage.RecordUse(style.Id);
            var copy = new CopyTextCommandEx(style, StyleCatalog.DefaultSample, onUsed: RecordUse);
            var copyKeepOpen = new CopyTextCommandEx(style, StyleCatalog.DefaultSample, keepOpen: true, onUsed: RecordUse);
            var togglePin = new TogglePinCommand(usage, style.Id);
            var details = new Details { Title = style.Name, Body = string.Empty };
            var item = new ListItem(copy)
            {
                Title = string.Empty,
                Subtitle = style.Name,
                Tags = [CategoryTags[style.Category]],
                Details = details,
                MoreCommands =
                [
                    new CommandContextItem(copyKeepOpen),
                    new CommandContextItem(togglePin),
                ],
            };
            _entries.Add(new StyleEntry
            {
                Style = style,
                Item = item,
                Copy = copy,
                CopyKeepOpen = copyKeepOpen,
                TogglePin = togglePin,
                Details = details,
            });
        }

        _noResultItem = new ListItem(new NoOpCommand())
        {
            Title = "没有适用于这段文字的样式",
            Subtitle = "试试输入拉丁字母、数字或常用汉字",
            Icon = NoResultIcon,
        };

        _usage.Changed += OnUsageChanged;
        Initialize();
    }

    internal TextStyleCategory? Category { get; }

    internal static string PageIdFor(TextStyleCategory? category) =>
        category is null ? AllStylesPageId : $"fancytext.styles.{category.Value.ToString().ToLowerInvariant()}";

    protected override void OnDisposing() => _usage.Changed -= OnUsageChanged;

    private void OnUsageChanged() => ScheduleRebuild(delayMs: 0);

    /// <summary>刷新持久列表项的属性并过滤出适用样式。不创建新的 ListItem/Command/Details。</summary>
    protected override IListItem[] BuildItems(string text)
    {
        var previewInput = TruncateTextElements(text, MaxPreviewInputChars);
        var inputTruncated = !string.Equals(previewInput, text, StringComparison.Ordinal);
        var truncatedNote = inputTruncated ? $"\n\n（预览仅前 {MaxPreviewInputChars} 字，回车复制的是**完整**转换结果）" : string.Empty;

        List<IListItem> result = [];
        foreach (var entry in _entries)
        {
            string preview;
            try
            {
                preview = entry.Style.Transform(previewInput);
            }
            catch (Exception)
            {
                continue; // 单个样式失败不影响整体列表
            }

            // 不适用即隐藏：与原文相同（如中文之于纯拉丁映射），或转换结果为空（如摩斯之于纯中文）
            if (string.IsNullOrEmpty(preview) || string.Equals(preview, previewInput, StringComparison.Ordinal))
            {
                continue;
            }

            var pinned = _usage.IsPinned(entry.Style.Id);
            entry.Copy.UpdateInput(text);
            entry.CopyKeepOpen.UpdateInput(text);
            entry.Item.Title = Preview(preview);
            entry.Item.Subtitle = pinned ? $"⭐ {entry.Style.Name}" : entry.Style.Name;
            entry.TogglePin.Name = pinned ? $"取消收藏「{entry.Style.Name}」" : $"收藏「{entry.Style.Name}」";
            entry.Details.Body = $"```\n{preview}\n```\n\n{entry.Style.Note ?? string.Empty}{truncatedNote}\n\n原文：{previewInput}";
            result.Add(entry.Item);
        }

        return result.Count == 0 ? [_noResultItem] : [.. result];
    }

    private static string Preview(string text)
    {
        var singleLine = text.ReplaceLineEndings(" ");
        return singleLine.Length <= MaxPreviewTitleChars ? singleLine : singleLine[..MaxPreviewTitleChars] + "…";
    }
}
