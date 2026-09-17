using System.Runtime.InteropServices;
using System.Windows;

namespace FancyText.Desktop.Helpers;

/// <summary>
/// 「预填加强」兜底：UIA 拿不到选中文字时，向目标应用发送 Ctrl+Insert（经典复制键）读取选中，
/// 读取后立即还原剪贴板。调用时机必须在唤出窗口之前（此刻目标应用仍持有前台）。
/// 用 Ctrl+Insert 而非 Ctrl+C：后者在终端里是 SIGINT 中断信号，会打断前台进程。
/// </summary>
internal static class ClipboardCopyReader
{
    private const int MaxLength = 4096;

    /// <summary>尝试经模拟复制取得选中文字；reason 输出失败原因（供诊断日志）。</summary>
    public static string? TryRead(out string reason)
    {
        reason = "剪贴板无文本";
        // 只备份文本：GetDataObject 拿到的 OLE 包装对象回设后会读取出错（CLIPBRD_E_BAD_DATA，实测）。
        // 纯文本走 CF_UNICODETEXT 往返最稳；非文本内容（图片/文件）无法保真还原，属已知限制。
        string? backupText = null;
        try
        {
            if (Clipboard.ContainsText())
            {
                backupText = Clipboard.GetText();
            }
        }
        catch (Exception)
        {
            // 剪贴板被其它进程短暂占用：继续尝试，读出为空时按原因返回
        }

        SendCopyChord();
        Thread.Sleep(160); // 等目标应用完成复制

        string? text = null;
        try
        {
            if (Clipboard.ContainsText())
            {
                text = Clipboard.GetText();
            }
        }
        catch (Exception ex)
        {
            reason = $"剪贴板读取异常 {ex.GetType().Name}";
        }

        // 剪贴板与复制前一致 = 目标没复制出任何内容（无选区/不支持 Ctrl+Insert），
        // 不能把旧剪贴板内容误当选中文字预填
        if (string.Equals(text, backupText, StringComparison.Ordinal))
        {
            text = null;
            reason = "目标未复制出内容（无选区或不支持该快捷键）";
        }

        // 立即还原（"不动剪贴板"的承诺只对最终状态负责）
        try
        {
            if (backupText is not null)
            {
                Clipboard.SetText(backupText);
            }
            else
            {
                Clipboard.Clear(); // 原本无文本：清掉这次复制，恢复原状
            }
        }
        catch (Exception)
        {
            // 还原失败不阻塞预填（用户剪贴板会残留这次复制的内容）
        }

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

    /// <summary>Ctrl+Insert：Windows 经典复制快捷键（覆盖面与 Ctrl+C 相当，但无终端中断语义）。</summary>
    private static void SendCopyChord()
    {
        var inputs = new[]
        {
            INPUT.Key(VK_LCONTROL, down: true),
            INPUT.Key(VK_INSERT, down: true),
            INPUT.Key(VK_INSERT, down: false),
            INPUT.Key(VK_LCONTROL, down: false),
        };
        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private const ushort VK_LCONTROL = 0xA2;
    private const ushort VK_INSERT = 0x2D;

    // Win32 INPUT 是 union（MOUSE/KEYBD/HARDWARE 取最大 32B），x64 总尺寸 40：type(4) + pad(4) + union(32)
    [StructLayout(LayoutKind.Explicit)]
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
}
