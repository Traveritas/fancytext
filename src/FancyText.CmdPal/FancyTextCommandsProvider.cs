using FancyText.CmdPal.Pages;
using FancyText.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace FancyText.CmdPal;

public sealed partial class FancyTextCommandsProvider : CommandProvider
{
    private readonly SharedTextState _shared = new();
    private readonly UsageState _usage = new();

    private readonly FancyTextStylesPage _allStylesPage;
    private readonly IReadOnlyDictionary<TextStyleCategory, FancyTextStylesPage> _categoryPages;
    private readonly FancyTextHomePage _home;

    public FancyTextCommandsProvider()
    {
        DisplayName = "花式文字";
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
                Title = "花式文字转换",
                Subtitle = "菊花体 / 魔鬼文字 / 花藤体 / 花体 / 火星文 … 按分类浏览 87 个样式",
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
            new FallbackCommandItem(new OpenHomePageCommand(), "花式文字转换")
            {
                Subtitle = "回车进入，转换这段文字为菊花体、魔鬼文字、花体等 87 个样式",
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
    public OpenHomePageCommand()
    {
        Name = "花式文字";
        Icon = new IconInfo("\uE8C8");
    }

    public override ICommandResult Invoke()
    {
        return CommandResult.GoToPage(new GoToPageArgs { PageId = Pages.FancyTextHomePage.HomePageId });
    }
}
