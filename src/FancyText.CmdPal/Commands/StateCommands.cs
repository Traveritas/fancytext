using FancyText.CmdPal.Helpers;
using FancyText.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace FancyText.CmdPal.Commands;

/// <summary>收藏/取消收藏一个样式，保持面板打开；星标状态经 <see cref="UsageState.Changed"/> 广播刷新。</summary>
internal sealed partial class TogglePinCommand : InvokableCommand
{
    private readonly UsageState _usage;
    private readonly string _styleId;

    public TogglePinCommand(UsageState usage, string styleId)
    {
        _usage = usage;
        _styleId = styleId;
        Icon = new IconInfo("\uE735"); // FavoriteStar
    }

    public override ICommandResult Invoke()
    {
        _usage.TogglePin(_styleId);
        return CommandResult.KeepOpen();
    }
}

/// <summary>跳转到某样式所在分类的二级页（按 PageId 导航）。</summary>
internal sealed partial class GoToCategoryCommand : InvokableCommand
{
    private readonly string _pageId;

    public GoToCategoryCommand(TextStyleCategory? category, AppLanguage lang)
    {
        _pageId = Pages.FancyTextStylesPage.PageIdFor(category);
        var displayName = category is { } c
            ? c.DisplayName(lang)
            : Loc.S(lang, "全部样式", "All styles");
        Name = Loc.S(lang, $"打开「{displayName}」分类", $"Open {displayName}");
        Icon = new IconInfo("\uE8C8");
    }

    public override ICommandResult Invoke()
    {
        return CommandResult.GoToPage(new GoToPageArgs { PageId = _pageId });
    }
}
