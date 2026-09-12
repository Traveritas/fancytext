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

    private static readonly ICommandResult CopyResult = CommandResult.ShowToast(new ToastArgs
    {
        Message = "已复制到剪贴板",
        Result = CommandResult.Hide(),
    });

    private static readonly ICommandResult CopyKeepOpenResult = CommandResult.ShowToast(new ToastArgs
    {
        Message = "已复制到剪贴板",
        Result = CommandResult.KeepOpen(),
    });

    private readonly TextStyle _style;
    private readonly Action? _onUsed;
    private readonly ICommandResult _result;
    private readonly bool _keepOpen;
    private string _input;

    public CopyTextCommandEx(TextStyle style, string input, bool keepOpen = false, Action? onUsed = null)
    {
        _style = style;
        _input = input;
        _onUsed = onUsed;
        _keepOpen = keepOpen;
        Name = keepOpen ? "复制并保持打开" : "复制";
        Icon = CopyIcon;
        _result = keepOpen ? CopyKeepOpenResult : CopyResult;
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
            return CommandResult.ShowToast(new ToastArgs
            {
                Message = "复制失败：剪贴板暂不可用，请重试",
                Result = _keepOpen ? CommandResult.KeepOpen() : CommandResult.Hide(),
            });
        }

        _onUsed?.Invoke();
        return _result;
    }
}
