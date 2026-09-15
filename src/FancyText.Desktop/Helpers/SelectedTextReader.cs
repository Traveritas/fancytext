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

    /// <summary>后台线程读取 + 300ms 超时：拿不到（不支持 UIA/无选中/目标挂起）返回 null，调用方自行回退。</summary>
    public static string? TryRead(TimeSpan? timeout = null)
    {
        try
        {
            var task = System.Threading.Tasks.Task.Run(ReadFromForegroundWindow);
            return task.Wait(timeout ?? TimeSpan.FromMilliseconds(300)) ? task.Result : null;
        }
        catch (Exception)
        {
            return null; // AggregateException（UIA COM 错误等）一律视为拿不到
        }
    }

    private static string? ReadFromForegroundWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        // TextPattern 通常由文本编辑子元素实现（顶层窗口自身一般不支持），向下找第一个支持的元素
        var root = AutomationElement.FromHandle(hwnd);
        var element = root.TryGetCurrentPattern(TextPattern.Pattern, out var rootPattern) && rootPattern is TextPattern
            ? root
            : root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.IsTextPatternAvailableProperty, true));
        if (element is null
            || !element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)
            || pattern is not TextPattern text)
        {
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

        return best;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
