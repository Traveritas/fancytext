using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace FancyText.Desktop.Helpers;

/// <summary>
/// 「预填兼容模式」兜底：UIA 拿不到选中文字时，向目标应用发送 Ctrl+Insert（经典复制键）读取选中，
/// 读取后立即还原剪贴板。必须在唤出窗口之前、UI 线程上调用（此刻目标应用仍持有前台；等待期靠
/// 泵消息放行剪贴板所有权握手，见 TryRead 内注释）。
/// 用 Ctrl+Insert 而非 Ctrl+C：后者在终端里是 SIGINT 中断信号，会打断前台进程。
/// </summary>
internal static class ClipboardCopyReader
{
    private const int MaxLength = 4096;
    private const int WaitMs = 600;      // 等目标复制出的墙钟上限（计时器分辨率不再放大它）
    private const int EarlyBailMs = 200; // 无复制迹象的提前放弃线：泵消息修好后实测健康复制 ~31ms 内落地，
                                         // 到此线序列号仍未动，几乎必是"无选区/目标不认这个快捷键"——
                                         // 等满 600ms 只会把"没选中想直接打字"的唤出白白拖慢

    /// <summary>尝试经模拟复制取得选中文字；reason 输出失败原因（供诊断日志）。</summary>
    public static string? TryRead(out string reason)
    {
        reason = "剪贴板无文本";
        // 只备份文本：GetDataObject 拿到的 OLE 包装对象回设后会读取出错（CLIPBRD_E_BAD_DATA，实测）。
        // 纯文本走 CF_UNICODETEXT 往返最稳；非文本内容（图片/文件）无法保真还原，属已知限制。
        string? backupText = null;
        var backupOk = true;
        try
        {
            if (Clipboard.ContainsText())
            {
                backupText = Clipboard.GetText();
            }
        }
        catch (Exception)
        {
            backupOk = false; // 剪贴板被其它进程占用：与"原本无文本"必须区分（否则既误填又毁数据）
        }

        if (!backupOk)
        {
            reason = "剪贴板被占用，放弃读取";
            return null;
        }

        // 备份后先清空再注入：剪贴板里若已躺着同一段文字（比如刚复制过、或上次尝试迟到的复制），
        // 这次复制成功也无法与旧内容区分，会被误判成"没复制出东西"。先清空，则等待窗口内出现的
        // 任何文本都只能来自这次复制。无文本时不清：剪贴板可能持有图片/文件等无法还原的内容。
        if (backupText is not null)
        {
            try
            {
                Clipboard.Clear();
            }
            catch (Exception)
            {
                reason = "剪贴板清空失败，放弃读取";
                return null;
            }
        }

        var sequenceBefore = GetClipboardSequenceNumber();
        if (!TrySendCopyChord(out var sendError))
        {
            Restore(backupText, changed: false);
            reason = sendError;
            return null;
        }

        // 轮询剪贴板序列号变化（而非固定 Sleep）：快时更快，慢目标也不会在还原后被"迟到的复制"覆盖。
        // 等待必须在泵消息中进行：剪贴板所有权握手是发给本线程（STA）的同步消息——目标应用执行复制时
        // 要先逐出旧所有者（上次的还原 SetText 让本进程成了所有者），不泵消息对方就被无限期挂死，
        // 序列号永不变化，成功的复制被误判成超时（实测阻塞等待 746ms 假超时，泵消息后 31ms 完成）。
        // 墙钟计时：Thread.Sleep(15) 的实际耗时被进程计时器分辨率放大，按次数算预算会漂到 1.7s。
        var copied = false;
        var wait = Stopwatch.StartNew();
        while (wait.ElapsedMilliseconds < WaitMs)
        {
            PumpMessages();
            if (GetClipboardSequenceNumber() != sequenceBefore)
            {
                copied = true;
                break;
            }

            if (wait.ElapsedMilliseconds >= EarlyBailMs)
            {
                break; // 提前放弃：见 EarlyBailMs 注释（下方宽限轮仍会接住"卡在握手中的迟到复制"）
            }

            Thread.Sleep(15);
        }

        if (!copied)
        {
            // 最后一轮泵可能刚好放行被卡的发送方：再泵一次并给一小段宽限，接住"迟到的复制"
            PumpMessages();
            Thread.Sleep(50);
            PumpMessages();
            copied = GetClipboardSequenceNumber() != sequenceBefore;
        }

        string? text = null;
        if (copied)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    text = Clipboard.GetText();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        reason = "目标复制出的是空文本";
                    }
                }
                else
                {
                    reason = "目标复制出的内容不含文本";
                }
            }
            catch (Exception ex)
            {
                reason = $"剪贴板读取异常 {ex.GetType().Name}";
            }
        }
        else
        {
            reason = "复制等待超时（目标未复制出内容：无选区或不支持该快捷键）";
        }

        Restore(backupText, changed: copied);

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Trim();
        if (text.Length > MaxLength)
        {
            text = text[..MaxLength];
        }

        reason = string.Empty;
        return text;
    }

    /// <summary>还原剪贴板（"不动剪贴板"的承诺只对最终状态负责）。changed=false 且原本无文本时
    /// 什么都不做：剪贴板没被动过，可能持有无法备份还原的图片/文件，动它反而毁数据。</summary>
    private static void Restore(string? backupText, bool changed)
    {
        try
        {
            if (backupText is not null)
            {
                Clipboard.SetText(backupText); // 我们清空过（或被复制顶掉过），还原备份文本
            }
            else if (changed)
            {
                Clipboard.Clear(); // 复制发生了且原本无文本可还原：清掉这次复制，恢复原状
            }
        }
        catch (Exception)
        {
            // 还原失败不阻塞预填（用户剪贴板会残留这次复制的内容）
        }
    }

    /// <summary>处理完当前排队的消息（WPF 版 DoEvents，只推进到 Background 优先级）。
    /// OLE 剪贴板握手是对本线程的同步消息，只有泵消息时才会被应答。</summary>
    private static void PumpMessages()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    /// <summary>Ctrl+Insert：Windows 经典复制快捷键（覆盖面与 Ctrl+C 相当，但无终端中断语义）。
    /// 注入前先把用户物理按住的修饰键（唤出热键通常是 Ctrl+Alt+Fx）合成 keyup 屏蔽——
    /// 否则严格匹配修饰键的应用会读成 Ctrl+Alt+Insert 而不识别为复制。</summary>
    private static bool TrySendCopyChord(out string error)
    {
        var inputs = new List<INPUT>(8);
        // 修饰键屏蔽：用户此刻通常还按着唤出热键的 Ctrl/Alt
        foreach (var (vk, held) in new (ushort, bool)[]
                 {
                     (VK_LCONTROL, IsKeyDown(VK_LCONTROL)),
                     (VK_RCONTROL, IsKeyDown(VK_RCONTROL)),
                     (VK_LMENU, IsKeyDown(VK_LMENU)), // Alt
                     (VK_RMENU, IsKeyDown(VK_RMENU)),
                     (VK_LSHIFT, IsKeyDown(VK_LSHIFT)),
                     (VK_RSHIFT, IsKeyDown(VK_RSHIFT)),
                 })
        {
            if (held)
            {
                inputs.Add(INPUT.Key(vk, down: false));
            }
        }

        inputs.Add(INPUT.Key(VK_LCONTROL, down: true));
        inputs.Add(INPUT.Key(VK_INSERT, down: true));
        inputs.Add(INPUT.Key(VK_INSERT, down: false));
        inputs.Add(INPUT.Key(VK_LCONTROL, down: false));

        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        if (sent != inputs.Count)
        {
            error = $"SendInput 失败 err={GetLastError()}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsKeyDown(ushort vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private const ushort VK_LCONTROL = 0xA2;
    private const ushort VK_RCONTROL = 0xA3;
    private const ushort VK_LMENU = 0xA4;
    private const ushort VK_RMENU = 0xA5;
    private const ushort VK_LSHIFT = 0xA0;
    private const ushort VK_RSHIFT = 0xA1;
    private const ushort VK_INSERT = 0x2D;

    // Win32 INPUT 是 union（MOUSE/KEYBD/HARDWARE 取最大 32B），x64 原生总尺寸 40：
    // type(4) + pad(4) + union(32)。Size 必须显式给 40——仅靠字段布局算出来是 32，
    // SendInput 会因 cbSize 不符返回 0 并报 ERROR_INVALID_PARAMETER（本功能曾因此整体静默失效）。
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;

        public static INPUT Key(ushort vk, bool down) => new()
        {
            type = 1, // INPUT_KEYBOARD
            ki = new KEYBDINPUT { wVk = vk, dwFlags = down ? 0u : 2u /* KEYEVENTF_KEYUP */ },
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("kernel32.dll")]
    private static extern uint GetLastError();
}
