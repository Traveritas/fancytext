using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace FancyText.Desktop.Helpers;

/// <summary>
/// 读取其它应用中当前选中的文字（UIA TextPattern，只读不动剪贴板）。
/// 覆盖 Chromium/Office/新版记事本/Windows Terminal/WPF 等主流应用；
/// 跨进程 UIA 查询可能因目标应用无响应而挂起，调用方务必走 <see cref="TryRead"/> 的超时保护。
/// </summary>
internal static class SelectedTextReader
{
    private const int MaxLength = 4096;

    /// <summary>后台线程读取 + 300ms 超时：拿不到（不支持 UIA/无选中/目标挂起）返回 null。
    /// reason 输出失败原因（供诊断日志）：定位问题靠它而不是猜。</summary>
    public static string? TryRead(out string? reason, TimeSpan? timeout = null)
    {
        reason = null;
        try
        {
            var localReason = string.Empty;
            var task = System.Threading.Tasks.Task.Run(() => ReadFromForegroundWindow(ref localReason));
            if (!task.Wait(timeout ?? TimeSpan.FromMilliseconds(300)))
            {
                reason = "超时（目标应用 UIA 无响应或树过大）";
                return null;
            }

            reason = string.IsNullOrEmpty(localReason) ? null : localReason;
            return task.Result;
        }
        catch (Exception ex)
        {
            var actual = ex is AggregateException { InnerException: { } inner } ? inner : ex; // Task.Wait 的聚合异常必须解包，否则诊断只剩 "AggregateException"
            reason = $"异常 {actual.GetType().Name}";
            return null;
        }
    }

    private static string? ReadFromForegroundWindow(ref string reason)
    {
        // 快路径：焦点元素即文本框。浏览器/终端的 UIA 树巨大，全树 FindFirst 慢且可能超时——
        // FocusedElement 是直达查询，也是地址栏/输入框场景的真正目标。
        AutomationElement? element = null;
        try
        {
            if (AutomationElement.FocusedElement is { } focused &&
                focused.TryGetCurrentPattern(TextPattern.Pattern, out var focusedPattern) &&
                focusedPattern is TextPattern)
            {
                element = focused;
            }
        }
        catch (Exception)
        {
            // 焦点元素跨进程探测失败：回落全树搜索
        }

        if (element is null)
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                reason = "前台窗口句柄为空";
                return null;
            }

            var root = AutomationElement.FromHandle(hwnd);
            element = root.TryGetCurrentPattern(TextPattern.Pattern, out var rootPattern) && rootPattern is TextPattern
                ? root
                : root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.IsTextPatternAvailableProperty, true));
            if (element is null)
            {
                reason = "目标不支持 UIA 文本接口";
                return null;
            }
        }

        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern) || pattern is not TextPattern text)
        {
            reason = "目标不支持 UIA 文本接口";
            return null;
        }

        var selection = text.GetSelection();
        string? best = null;
        foreach (TextPatternRange range in selection)
        {
            var rangeText = range.GetText(-1);
            if (string.IsNullOrWhiteSpace(rangeText))
            {
                continue;
            }

            rangeText = rangeText.Trim();
            if (rangeText.Length > MaxLength)
            {
                rangeText = rangeText[..MaxLength];
            }

            if (best is null || rangeText.Length > best.Length)
            {
                best = rangeText; // 多段选区取最长的一段
            }
        }

        if (best is null)
        {
            reason = "无选中内容（或热键的 Alt 键让目标应用丢了选区）";
        }

        return best;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
