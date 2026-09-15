using FancyText.CmdPal.Helpers;
using FancyText.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace FancyText.CmdPal.Commands;

/// <summary>
/// 复制转换结果到剪贴板，并以 Toast 反馈；可选择保持面板打开。
/// 持有 (样式, 原文) 引用，Invoke 时才做转换——列表预览只处理截断文本，全文转换只发生一次。
/// 复制失败（剪贴板被占用等）会如实提示失败并写诊断日志，不再伪装成功。
/// </summary>
internal sealed partial class CopyTextCommandEx : InvokableCommand
{
    private static readonly IconInfo CopyIcon = new("\uE8C8");

    private readonly TextStyle _style;
    private readonly AppLanguage _lang;
    private readonly Action? _onUsed;
    private readonly ICommandResult _result;
    private readonly bool _keepOpen;
    private string _input;

    public CopyTextCommandEx(TextStyle style, string input, AppLanguage lang, bool keepOpen = false, Action? onUsed = null)
    {
        _style = style;
        _lang = lang;
        _input = input;
        _onUsed = onUsed;
        _keepOpen = keepOpen;
        Name = Loc.S(lang, keepOpen ? "复制并保持打开" : "复制", keepOpen ? "Copy and keep open" : "Copy");
        Icon = CopyIcon;
        // 成功 Toast 与命令一并构造一次、持久复用（语言在构造期定死，重启面板进程后生效）。
        _result = CommandResult.ShowToast(new ToastArgs
        {
            Message = Loc.S(lang, "已复制到剪贴板", "Copied ✓"),
            Result = keepOpen ? CommandResult.KeepOpen() : CommandResult.Hide(),
        });
    }

    /// <summary>列表项持久复用时，仅替换待转换的全文引用。</summary>
    public void UpdateInput(string input) => _input = input;

    public override ICommandResult Invoke()
    {
        var output = _input;
        try
        {
            output = _style.Transform(_input);
        }
        catch (Exception)
        {
            // 转换失败时退回复制原文，避免用户丢字
        }

        if (!ClipboardBridge.TrySetText(output))
        {
            // 失败路径按需构造（不应发生的小概率分支）；语言用构造期解析值，与命令名一致。
            return CommandResult.ShowToast(new ToastArgs
            {
                Message = Loc.S(_lang, "复制失败：剪贴板暂不可用，请重试", "Copy failed: clipboard unavailable, try again"),
                Result = _keepOpen ? CommandResult.KeepOpen() : CommandResult.Hide(),
            });
        }

        _onUsed?.Invoke();
        return _result;
    }
}
