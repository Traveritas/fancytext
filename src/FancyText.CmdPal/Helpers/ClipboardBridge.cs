using Microsoft.CommandPalette.Extensions.Toolkit;

namespace FancyText.CmdPal.Helpers;

/// <summary>
/// 剪贴板读写。写入走 Toolkit 的 ClipboardHelper（Win32 OpenClipboard 路径）——
/// WinRT DataPackage/Clipboard.SetContent 在扩展的 COM 后台线程上会抛 COMException，
/// v1.0.0 的"复制不起作用"即源于此。所有失败都写诊断日志并向调用方如实返回。
/// </summary>
internal static class ClipboardBridge
{
    public static bool TrySetText(string text)
    {
        try
        {
            ClipboardHelper.SetText(text);
            return true;
        }
        catch (Exception ex)
        {
            DiagLog.Append($"clipboard-set 失败: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public static string? TryGetText()
    {
        try
        {
            var text = ClipboardHelper.GetText();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (Exception ex)
        {
            DiagLog.Append($"clipboard-get 失败: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
