using FancyText.CmdPal.Pages;
using FancyText.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace FancyText.CmdPal;

public sealed partial class FancyTextCommandsProvider : CommandProvider
{
    private readonly SharedTextState _shared = new();
    private readonly UsageState _usage = new();

    /// <summary>语言在构造期解析一次（桌面版切语言后，面板进程重启时生效）。</summary>
    private readonly AppLanguage _lang;

    private readonly FancyTextStylesPage _allStylesPage;
    private readonly IReadOnlyDictionary<TextStyleCategory, FancyTextStylesPage> _categoryPages;
    private readonly FancyTextHomePage _home;

    public FancyTextCommandsProvider()
    {
        _lang = Localization.Resolve(_usage.Language);
        DisplayName = Loc.S(_lang, "花式文字", "Fancy Text");
        Id = "FancyText";
        Icon = new IconInfo("\uE8C8");

        _allStylesPage = new FancyTextStylesPage(_shared, _usage, category: null);
        _categoryPages = Enum.GetValues<TextStyleCategory>()
            .ToDictionary(c => c, c => new FancyTextStylesPage(_shared, _usage, c));
        _home = new FancyTextHomePage(_shared, _usage, _allStylesPage, _categoryPages);
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return
        [
            new CommandItem(_home)
            {
                Title = Loc.S(_lang, "花式文字转换", "Convert to fancy text"),
                Subtitle = Loc.S(
                    _lang,
                    $"菊花体 / 魔鬼文字 / 花藤体 / 花体 / 火星文 … 按分类浏览 {StyleCatalog.All.Count} 个样式",
                    $"Chrysanthemum / Zalgo / Vine / Script / Martian … {StyleCatalog.All.Count} styles by category"),
                Icon = new IconInfo("\uE8C8"),
            },
        ];
    }

    public override IFallbackCommandItem[] FallbackCommands()
    {
        // 在 CmdPal 根搜索框直接输入文字时出现本项；query 经 FallbackHandler 写入共享文本，
        // 首页与各分类页实时跟随根搜索框内容。
        return
        [
            new FallbackCommandItem(new OpenHomePageCommand(_lang), Loc.S(_lang, "花式文字转换", "Convert to fancy text"))
            {
                Subtitle = Loc.S(
                    _lang,
                    $"回车进入，转换这段文字为菊花体、魔鬼文字、花体等 {StyleCatalog.All.Count} 个样式",
                    $"Press Enter to convert this text with {StyleCatalog.All.Count} styles"),
                Icon = new IconInfo("\uE8C8"),
                FallbackHandler = new RootQueryHandler(_shared),
            },
        ];
    }
}

/// <summary>把 CmdPal 根搜索框的 query 写入共享文本（空 query 清除覆盖，回落到剪贴板）。</summary>
internal sealed partial class RootQueryHandler : IFallbackHandler
{
    private readonly SharedTextState _shared;

    public RootQueryHandler(SharedTextState shared)
    {
        _shared = shared;
    }

    public void UpdateQuery(string query)
    {
        _shared.Text = string.IsNullOrWhiteSpace(query) ? null : query;
    }
}

internal sealed partial class OpenHomePageCommand : InvokableCommand
{
    public OpenHomePageCommand(AppLanguage lang)
    {
        Name = Loc.S(lang, "花式文字", "Fancy Text");
        Icon = new IconInfo("\uE8C8");
    }

    public override ICommandResult Invoke()
    {
        return CommandResult.GoToPage(new GoToPageArgs { PageId = Pages.FancyTextHomePage.HomePageId });
    }
}
