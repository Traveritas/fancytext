using FancyText.CmdPal.Commands;
using FancyText.CmdPal.Helpers;
using FancyText.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace FancyText.CmdPal.Pages;

/// <summary>
/// 一级首页：收藏/最近使用快速条目（回车直接复制）+ 分类入口（+「全部样式」）。
/// 每个分类条目的副标题实时预览代表样式与可用数量；解析出的文本写入 <see cref="SharedTextState"/> 供二级页使用。
/// </summary>
internal sealed partial class FancyTextHomePage : DebouncedTextPageBase
{
    public const string HomePageId = "fancytext.home";

    private const int MaxPreviewInputChars = 64;
    private const int MaxRepPreviewChars = 48;
    private const int MaxQuickTitleChars = 96;

    private static readonly IconInfo PageIcon = new("\uE8C8");

    /// <summary>分类入口的持久 UI 对象（Item 与 Details 共用同一实例，刷新只改属性）。</summary>
    private sealed class Entry
    {
        public required ListItem Item { get; init; }
        public required Details Details { get; init; }
        public required IReadOnlyList<TextStyle> Styles { get; init; }
        public required IReadOnlyList<TextStyle> Representatives { get; init; }
        public TextStyleCategory? Category { get; init; }
    }

    /// <summary>收藏/最近快速条目（按样式 ID 缓存，持久复用）。</summary>
    private sealed class QuickEntry
    {
        public required TextStyle Style { get; init; }
        public required ListItem Item { get; init; }
        public required CopyTextCommandEx Copy { get; init; }
        public required CopyTextCommandEx CopyKeepOpen { get; init; }
        public required TogglePinCommand TogglePin { get; init; }
        public required Details Details { get; init; }
    }

    private readonly UsageState _usage;
    private readonly AppLanguage _lang;
    private readonly Tag _pinnedTag;
    private readonly Tag _recentTag;
    private readonly List<Entry> _entries = [];
    private readonly Dictionary<string, QuickEntry> _quickById = [];
    private readonly IReadOnlyDictionary<string, TextStyle> _stylesById;

    public FancyTextHomePage(
        SharedTextState shared,
        UsageState usage,
        FancyTextStylesPage allStylesPage,
        IReadOnlyDictionary<TextStyleCategory, FancyTextStylesPage> categoryPages)
        : base(shared, diagTag: "Home", readSharedText: true)
    {
        _usage = usage;
        _lang = Localization.Resolve(usage.Language);
        _pinnedTag = new Tag(Loc.S(_lang, "收藏", "Pinned"));
        _recentTag = new Tag(Loc.S(_lang, "最近", "Recent"));
        _stylesById = StyleCatalog.All.ToDictionary(s => s.Id, s => s);

        Id = HomePageId;
        Icon = PageIcon;
        Name = Loc.S(_lang, "花式文字", "Fancy Text");
        Title = Loc.S(_lang, "花式文字转换", "Convert to fancy text");
        PlaceholderText = Loc.S(_lang, "输入或粘贴要转换的文字（留空则使用剪贴板）", "Type or paste text to convert (uses clipboard if empty)");

        // 「全部样式」入口（Category = null）
        AddEntry(null, Loc.S(_lang, "全部样式", "All styles"), Loc.S(_lang, $"{StyleCatalog.All.Count} 个样式，一页浏览", $"{StyleCatalog.All.Count} styles on one page"), allStylesPage, StyleCatalog.All, []);

        // 分类入口
        foreach (var category in Enum.GetValues<TextStyleCategory>())
        {
            var styles = StyleCatalog.All.Where(s => s.Category == category).ToList();
            var representatives = RepresentativeIds(category)
                .Select(FindStyle)
                .Where(s => s is not null)
                .Cast<TextStyle>()
                .ToList();
            AddEntry(category, category.DisplayName(_lang), Loc.S(_lang, $"{styles.Count} 个样式", $"{styles.Count} styles"), categoryPages[category], styles, representatives);
        }

        _usage.Changed += OnUsageChanged;
        Initialize();
    }

    protected override void OnDisposing() => _usage.Changed -= OnUsageChanged;

    private void OnUsageChanged() => ScheduleRebuild(delayMs: 0);

    private void AddEntry(
        TextStyleCategory? category,
        string title,
        string initialSubtitle,
        ICommand navigationTarget,
        IReadOnlyList<TextStyle> styles,
        IReadOnlyList<TextStyle> representatives)
    {
        var details = new Details { Title = title, Body = string.Empty };
        var item = new ListItem(navigationTarget)
        {
            Title = title,
            Subtitle = initialSubtitle,
            Icon = PageIcon,
            Details = details,
        };
        _entries.Add(new Entry { Item = item, Details = details, Styles = styles, Representatives = representatives, Category = category });
    }

    private QuickEntry GetOrCreateQuickEntry(TextStyle style)
    {
        if (_quickById.TryGetValue(style.Id, out var existing))
        {
            return existing;
        }

        void RecordUse() => _usage.RecordUse(style.Id);
        var copy = new CopyTextCommandEx(style, StyleCatalog.DefaultSample, _lang, onUsed: RecordUse);
        var copyKeepOpen = new CopyTextCommandEx(style, StyleCatalog.DefaultSample, _lang, keepOpen: true, onUsed: RecordUse);
        var togglePin = new TogglePinCommand(_usage, style.Id);
        var details = new Details { Title = style.GetName(_lang), Body = string.Empty };
        var item = new ListItem(copy)
        {
            Title = string.Empty,
            Subtitle = style.GetName(_lang),
            Icon = PageIcon,
            Details = details,
            MoreCommands =
            [
                new CommandContextItem(copyKeepOpen),
                new CommandContextItem(new GoToCategoryCommand(style.Category, _lang)),
                new CommandContextItem(togglePin),
            ],
        };
        var entry = new QuickEntry { Style = style, Item = item, Copy = copy, CopyKeepOpen = copyKeepOpen, TogglePin = togglePin, Details = details };
        _quickById[style.Id] = entry;
        return entry;
    }

    protected override void OnTextResolved(string text) => Shared.Text = text;

    protected override IListItem[] BuildItems(string text)
    {
        var previewInput = TruncateTextElements(text, MaxPreviewInputChars);

        List<IListItem> result = [];

        // —— 收藏（回车直接复制当前文本的转换结果）——
        foreach (var styleId in _usage.Pinned)
        {
            if (FindStyle(styleId) is not { } style)
            {
                continue;
            }

            if (TryUpdateQuickEntry(GetOrCreateQuickEntry(style), previewInput, text, _pinnedTag, pinned: true))
            {
                result.Add(GetOrCreateQuickEntry(style).Item);
            }
        }

        // —— 最近使用（不含已收藏，最多 8 个）——
        var recentCount = 0;
        foreach (var styleId in _usage.Recent)
        {
            if (recentCount >= 8 || _usage.IsPinned(styleId))
            {
                continue;
            }

            if (FindStyle(styleId) is not { } style)
            {
                continue;
            }

            if (TryUpdateQuickEntry(GetOrCreateQuickEntry(style), previewInput, text, _recentTag, pinned: false))
            {
                result.Add(GetOrCreateQuickEntry(style).Item);
                recentCount++;
            }
        }

        // —— 分类入口 ——
        var applicableByCategory = CountApplicable(previewInput);

        foreach (var entry in _entries)
        {
            var body = Loc.S(
                _lang,
                $"当前文本：{previewInput}\n\n包含样式：{string.Join("、", entry.Styles.Select(s => s.Name).Take(12))}",
                $"Current text: {previewInput}\n\nStyles: {string.Join(", ", entry.Styles.Select(s => s.GetName(_lang)).Take(12))}");
            string subtitle;
            if (entry.Category is null)
            {
                var applicable = applicableByCategory.Values.Sum();
                subtitle = Loc.S(_lang, $"{entry.Styles.Count} 个样式 · 当前文本可用 {applicable} 个", $"{entry.Styles.Count} styles · {applicable} apply to this text");
            }
            else
            {
                var applicable = applicableByCategory[entry.Category.Value];
                var reps = new List<string>();
                foreach (var rep in entry.Representatives)
                {
                    try
                    {
                        var preview = rep.Transform(previewInput);
                        if (!string.Equals(preview, previewInput, StringComparison.Ordinal))
                        {
                            reps.Add(OneLine(preview));
                        }
                    }
                    catch (Exception)
                    {
                        // 忽略
                    }
                }

                var repText = string.Join("  ", reps);
                if (repText.Length > MaxRepPreviewChars)
                {
                    repText = repText[..MaxRepPreviewChars] + "…";
                }

                subtitle = applicable == 0
                    ? Loc.S(_lang, $"{entry.Styles.Count} 个样式 · 当前文本无适用样式", $"{entry.Styles.Count} styles · none apply to this text")
                    : Loc.S(_lang, $"{entry.Styles.Count} 个样式 · 可用 {applicable} · {repText}", $"{entry.Styles.Count} styles · {applicable} available · {repText}");
                if (reps.Count > 0)
                {
                    body += Loc.S(
                        _lang,
                        $"\n\n效果示例：\n```\n{string.Join("\n", reps)}\n```",
                        $"\n\nExamples:\n```\n{string.Join("\n", reps)}\n```");
                }
            }

            entry.Item.Subtitle = subtitle;
            entry.Details.Body = body;
            result.Add(entry.Item);
        }

        return [.. result];
    }

    /// <summary>更新快速条目内容；若该样式对当前文本不适用则返回 false（不展示）。</summary>
    private bool TryUpdateQuickEntry(QuickEntry entry, string previewInput, string fullText, Tag tag, bool pinned)
    {
        string preview;
        try
        {
            preview = entry.Style.Transform(previewInput);
        }
        catch (Exception)
        {
            return false;
        }

        if (string.IsNullOrEmpty(preview) || string.Equals(preview, previewInput, StringComparison.Ordinal))
        {
            return false;
        }

        var singleLine = preview.ReplaceLineEndings(" ");
        if (singleLine.Length > MaxQuickTitleChars)
        {
            singleLine = singleLine[..MaxQuickTitleChars] + "…";
        }

        entry.Copy.UpdateInput(fullText);
        entry.CopyKeepOpen.UpdateInput(fullText);
        var styleName = entry.Style.GetName(_lang);
        entry.Item.Title = singleLine;
        entry.Item.Subtitle = pinned ? $"⭐ {styleName}" : styleName;
        entry.Item.Tags = [tag];
        entry.TogglePin.Name = pinned
            ? Loc.S(_lang, $"取消收藏「{styleName}」", $"Unpin {styleName}")
            : Loc.S(_lang, $"收藏「{styleName}」", $"Pin {styleName}");
        entry.Details.Body = $"```\n{preview}\n```\n\n{entry.Style.GetNote(_lang) ?? string.Empty}\n\n{Loc.S(_lang, "原文：", "Input:")}{previewInput}";
        return true;
    }

    private static Dictionary<TextStyleCategory, int> CountApplicable(string previewInput)
    {
        var counts = new Dictionary<TextStyleCategory, int>();
        foreach (var group in StyleCatalog.All.GroupBy(s => s.Category))
        {
            var count = 0;
            foreach (var style in group)
            {
                try
                {
                    var output = style.Transform(previewInput);
                    if (!string.IsNullOrEmpty(output) && !string.Equals(output, previewInput, StringComparison.Ordinal))
                    {
                        count++;
                    }
                }
                catch (Exception)
                {
                    // 忽略单个样式异常
                }
            }

            counts[group.Key] = count;
        }

        return counts;
    }

    private TextStyle? FindStyle(string id) => _stylesById.TryGetValue(id, out var style) ? style : null;

    /// <summary>每个分类挑 2 个代表样式用于首页预览。</summary>
    private static IEnumerable<string> RepresentativeIds(TextStyleCategory category) => category switch
    {
        TextStyleCategory.CjkEffect => ["juhua-1", "vine-1"],
        TextStyleCategory.LatinFancy => ["bold-script", "circled"],
        TextStyleCategory.Decoration => ["wing-classic", "border-star"],
        TextStyleCategory.Transform => ["upside-down", "leet"],
        TextStyleCategory.Chinese => ["martian"],
        TextStyleCategory.Encoding => ["base64"],
        _ => [],
    };

    private static string OneLine(string text) => text.ReplaceLineEndings(" ").Trim();
}
