using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace FancyText.Desktop.Helpers;

/// <summary>
/// 读取其它应用中当前选中的文字（UIA TextPattern，只读不动剪贴板）。
/// 覆盖 Chromium/Office/新版记事本/Windows Terminal/WPF 等主流应用；
/// 跨进程 UIA 查询可能因目标应用无响应而挂起，调用方务必走超时保护。
/// </summary>
internal static class SelectedTextReader
{
    private const int MaxLength = 4096;

    /// <summary>读取结果类别：调用方据此决策（如"权威无选中"时跳过模拟复制兜底）。</summary>
    internal enum ReadOutcome
    {
        GotText,     // 取到选中文字
        NoSelection, // 目标支持文本接口且判定无选中——结论权威，无需再试模拟复制
        Unsupported, // 目标不支持 UIA 文本接口（模拟复制的适用场景）
        Timeout,     // 目标无响应或树过大，未知状态
        Error,       // 异常/拿不到前台窗口
    }

    /// <summary>后台线程读取 + 超时保护：拿不到返回 null，reason 供诊断日志，outcome 区分失败类别。</summary>
    public static string? TryRead(out string? reason, out ReadOutcome outcome, TimeSpan? timeout = null)
    {
        reason = null;
        outcome = ReadOutcome.Error;
        try
        {
            var localReason = string.Empty;
            var localOutcome = ReadOutcome.Error;
            var task = Task.Run(() => ReadFromForegroundWindow(IntPtr.Zero, ref localReason, out localOutcome));
            if (!task.Wait(timeout ?? TimeSpan.FromMilliseconds(300)))
            {
                reason = "超时（目标应用 UIA 无响应或树过大）";
                outcome = ReadOutcome.Timeout;
                return null;
            }

            reason = string.IsNullOrEmpty(localReason) ? null : localReason;
            outcome = localOutcome;
            return task.Result;
        }
        catch (Exception ex)
        {
            var actual = ex is AggregateException { InnerException: { } inner } ? inner : ex; // Task.Wait 的聚合异常必须解包，否则诊断只剩 "AggregateException"
            reason = $"异常 {actual.GetType().Name}";
            outcome = ReadOutcome.Error;
            return null;
        }
    }

    /// <summary>异步版（必须在唤出窗口 Show 之前调用）：此刻同步捕获目标前台窗口句柄，后台读取
    /// 期间本进程弹窗可能已夺走焦点——快路径凭该句柄校验 FocusedElement 仍属目标进程才可信，
    /// 否则会读到自己的输入框（TextPattern 可用、永远"无选中"），静默毁掉预填。
    /// 完成（或超时）后把结果投递回调用线程的同步上下文（UI 线程）；被超时放弃的查询随后完成则丢弃。</summary>
    public static void TryReadAsync(TimeSpan timeout, Action<string?, string?, ReadOutcome> onCompleted)
    {
        var foreground = GetForegroundWindow();
        var sync = SynchronizationContext.Current;
        _ = Task.Run(async () =>
        {
            string? text = null;
            string? reason = null;
            var outcome = ReadOutcome.Timeout;
            try
            {
                var localReason = string.Empty;
                var read = Task.Run(() => ReadFromForegroundWindow(foreground, ref localReason, out outcome));
                if (await Task.WhenAny(read, Task.Delay(timeout)) == read)
                {
                    text = read.Result;
                    reason = string.IsNullOrEmpty(localReason) ? null : localReason;
                }
                else
                {
                    reason = "超时（目标应用 UIA 无响应或树过大）";
                }
            }
            catch (Exception ex)
            {
                var actual = ex is AggregateException { InnerException: { } inner } ? inner : ex;
                reason = $"异常 {actual.GetType().Name}";
                outcome = ReadOutcome.Error;
            }

            if (sync is not null)
            {
                sync.Post(_ => onCompleted(text, reason, outcome), null); // 回 UI 线程操作输入框
            }
            else
            {
                onCompleted(text, reason, outcome);
            }
        });
    }

    private static string? ReadFromForegroundWindow(IntPtr knownForeground, ref string reason, out ReadOutcome outcome)
    {
        outcome = ReadOutcome.Unsupported;

        // 快路径：焦点元素即文本框。浏览器/终端的 UIA 树巨大，全树 FindFirst 慢且可能超时——
        // FocusedElement 是直达查询，也是地址栏/输入框场景的真正目标。
        AutomationElement? element = null;
        try
        {
            if (AutomationElement.FocusedElement is { } focused &&
                BelongsToForeground(focused, knownForeground) &&
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
            var hwnd = knownForeground != IntPtr.Zero ? knownForeground : GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                reason = "前台窗口句柄为空";
                outcome = ReadOutcome.Error;
                return null;
            }

            var root = AutomationElement.FromHandle(hwnd);
            element = root.TryGetCurrentPattern(TextPattern.Pattern, out var rootPattern) && rootPattern is TextPattern
                ? root
                : root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.IsTextPatternAvailableProperty, true));
            if (element is null)
            {
                reason = "目标不支持 UIA 文本接口";
                outcome = ReadOutcome.Unsupported;
                return null;
            }
        }

        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern) || pattern is not TextPattern text)
        {
            reason = "目标不支持 UIA 文本接口";
            outcome = ReadOutcome.Unsupported;
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
            outcome = ReadOutcome.NoSelection;
            return null;
        }

        outcome = ReadOutcome.GotText;
        return best;
    }

    /// <summary>校验 FocusedElement 仍属于捕获的前台窗口进程（knownForeground 为零 = 同步路径，
    /// 行为与旧版一致不校验）。</summary>
    private static bool BelongsToForeground(AutomationElement element, IntPtr knownForeground)
    {
        if (knownForeground == IntPtr.Zero)
        {
            return true;
        }

        try
        {
            GetWindowThreadProcessId(knownForeground, out var pid);
            return element.Current.ProcessId == pid;
        }
        catch (Exception)
        {
            return false; // 元素已消失等：宁可回落树内查找
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
