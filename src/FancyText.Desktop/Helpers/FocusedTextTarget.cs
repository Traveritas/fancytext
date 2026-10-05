using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace FancyText.Desktop.Helpers;

internal enum InsertOutcome { Inserted, Unavailable, FocusChanged, KeysHeld, TooLong, UnsupportedText, Blocked, Partial }

/// <summary>记住唤出前的编辑控件；恢复前台后再次校验同一控件，再发送 Unicode 输入。</summary>
internal sealed class FocusedTextTarget
{
    internal const int MaxInputLength = 32768;
    private static readonly SemaphoreSlim ProbeGate = new(1, 1);
    private readonly IntPtr _window;
    private readonly IntPtr _focus;
    private readonly uint _processId;
    private readonly int[] _runtimeId;

    private FocusedTextTarget(IntPtr window, IntPtr focus, uint processId, int[] runtimeId)
    {
        _window = window;
        _focus = focus;
        _processId = processId;
        _runtimeId = runtimeId;
    }

    /// <summary>只在启用直接输入时调用，且必须在 Show 之前；UIA 查询有界且不累积挂起的线程。</summary>
    public static FocusedTextTarget? Capture()
    {
        var window = GetForegroundWindow();
        var focus = GetFocusWindow(window, out var processId);
        if (focus == IntPtr.Zero || processId == Environment.ProcessId)
        {
            return null;
        }

        return ProbeAsync(() =>
        {
            var element = AutomationElement.FocusedElement;
            if (element is null || !IsEditable(element) || element.Current.ProcessId != processId ||
                GetForegroundWindow() != window || GetFocusWindow(window, out _) != focus)
            {
                return null;
            }
            return new FocusedTextTarget(window, focus, processId, element.GetRuntimeId());
        }).GetAwaiter().GetResult();
    }

    public bool IsForeground => GetForegroundWindow() == _window;

    /// <summary>不强行附着输入线程或重设控件焦点；窗口自然恢复的焦点必须仍是原控件。</summary>
    public bool TryActivate()
    {
        // 非活动线程的 hwndFocus 可能为空；精确控件校验必须在恢复前台之后进行。
        if (GetWindowThreadProcessId(_window, out var processId) == 0 || processId != _processId)
        {
            return false;
        }
        return SetForegroundWindow(_window);
    }

    public async Task<InsertOutcome> InsertAsync(string text)
    {
        if (GetTextIssue(text) is { } issue)
        {
            return issue;
        }

        var matches = await ProbeAsync(() =>
        {
            var element = AutomationElement.FocusedElement;
            return element is not null && element.Current.ProcessId == _processId &&
                element.GetRuntimeId().AsSpan().SequenceEqual(_runtimeId) && IsEditable(element);
        });
        if (!matches || !IsForeground || GetFocusWindow(_window, out var processId) != _focus || processId != _processId)
        {
            return InsertOutcome.FocusChanged;
        }
        if (ActivationKeysHeld())
        {
            return InsertOutcome.KeysHeld;
        }

        // UTF-16 逐码元按下/松开，代理对与组合符保持原序；整批送入，避免分批期间切换应用。
        // 不使用剪贴板，也不发送 Ctrl+V / 物理 Enter。
        var inputs = new INPUT[text.Length * 2];
        for (var i = 0; i < text.Length; i++)
        {
            inputs[i * 2] = INPUT.Unicode(text[i], up: false);
            inputs[i * 2 + 1] = INPUT.Unicode(text[i], up: true);
        }
        // 构造长结果也需要时间，紧挨着 SendInput 再查一次前台和按键。
        if (!IsForeground || GetFocusWindow(_window, out _) != _focus)
        {
            return InsertOutcome.FocusChanged;
        }
        if (ActivationKeysHeld())
        {
            return InsertOutcome.KeysHeld;
        }
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return sent == inputs.Length ? InsertOutcome.Inserted : sent == 0 ? InsertOutcome.Blocked : InsertOutcome.Partial;
    }

    internal static bool ActivationKeysHeld() =>
        IsDown(0x0D) || IsDown(0x01) || IsDown(0x10) || IsDown(0x11) || IsDown(0x12) || IsDown(0x5B) || IsDown(0x5C);

    // Unicode 输入会被部分控件静默丢弃换行/制表符；不能改发 Enter/Tab，避免提交表单或切走焦点。
    internal static InsertOutcome? GetTextIssue(string text) => text.Length switch
    {
        0 => InsertOutcome.Unavailable,
        > MaxInputLength => InsertOutcome.TooLong,
        _ => text.Any(char.IsControl) ? InsertOutcome.UnsupportedText : null,
    };

    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private static bool IsEditable(AutomationElement element)
    {
        if (!element.Current.IsEnabled || element.Current.IsPassword || !element.Current.HasKeyboardFocus)
        {
            return false;
        }
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value) && value is ValuePattern vp)
        {
            return !vp.Current.IsReadOnly;
        }
        return element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern) && pattern is TextPattern text &&
            text.DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute) is false;
    }

    private static async Task<T?> ProbeAsync<T>(Func<T> read)
    {
        if (!ProbeGate.Wait(0))
        {
            return default;
        }
        var probe = Task.Run(() =>
        {
            try { return read(); }
            catch (Exception) { return default; } // 控件销毁、拒绝访问、UIA 不支持等
            finally { ProbeGate.Release(); }
        });
        // 超时后任务只能继续只读查询，绝不从后台延迟写入外部应用。
        return await Task.WhenAny(probe, Task.Delay(300)).ConfigureAwait(false) == probe
            ? await probe.ConfigureAwait(false) : default;
    }

    private static IntPtr GetFocusWindow(IntPtr window, out uint processId)
    {
        var thread = GetWindowThreadProcessId(window, out processId);
        var info = new GUITHREADINFO { Size = Marshal.SizeOf<GUITHREADINFO>() };
        return thread != 0 && GetGUIThreadInfo(thread, ref info) ? info.Focus : IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int Size;
        public uint Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }

    // x64/ARM64 INPUT：4 字节类型 + 4 字节对齐 + 32 字节联合体。
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ushort VirtualKey;
        [FieldOffset(10)] public ushort Scan;
        [FieldOffset(12)] public uint Flags;

        public static INPUT Unicode(char ch, bool up) => new() { Type = 1, Scan = ch, Flags = up ? 6u : 4u };
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
