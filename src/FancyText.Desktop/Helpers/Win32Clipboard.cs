using System.Runtime.InteropServices;

namespace FancyText.Desktop.Helpers;

/// <summary>
/// 剪贴板访问：原生 Win32 路径（Open/Get/Empty/Set/Close），桌面版的剪贴板读写都经这里，不走 WPF 的 OLE 通路。
/// 起因：实测 OLE 通路会周期性卡死——Clipboard.SetText 每次自旋约 0.9s 后抛 CLIPBRD_E_CANT_OPEN，而同进程内
/// 交替实测原生路径同时刻全部成功；且卡死状态下 OLE 写入其实已（延迟渲染）进了剪贴板、界面却报"写入失败"，
/// 形成"报错但能粘贴"的假失败。原生路径的取舍：写入即静态数据（无需 Flush，本进程退出后照样可粘贴）、
/// 失败原子且真实；代价是只处理 CF_UNICODETEXT（窄字符格式由系统按需自动合成）。
/// 全部接口不抛异常：失败经返回值 / reason 如实回传，由调用方决定如何记录与降级。
/// </summary>
internal static class Win32Clipboard
{
    private const uint CF_TEXT = 1;
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    /// <summary>剪贴板是否有文本。Unicode / ANSI 两种格式都认：Windows 会在两者间自动合成，只查其一会漏。</summary>
    public static bool ContainsText() =>
        IsClipboardFormatAvailable(CF_UNICODETEXT) || IsClipboardFormatAvailable(CF_TEXT);

    /// <summary>读取纯文本；无文本或剪贴板被占用时返回 null 并给出 reason。</summary>
    public static string? TryGetText(out string reason)
    {
        reason = string.Empty;
        if (!OpenClipboard(IntPtr.Zero))
        {
            reason = $"OpenClipboard 失败 err={Marshal.GetLastWin32Error()}";
            return null;
        }

        try
        {
            var hData = GetClipboardData(CF_UNICODETEXT);
            if (hData == IntPtr.Zero)
            {
                reason = "剪贴板无 Unicode 文本";
                return null;
            }

            var source = GlobalLock(hData);
            if (source == IntPtr.Zero)
            {
                reason = $"GlobalLock 失败 err={Marshal.GetLastWin32Error()}";
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(source);
            }
            finally
            {
                GlobalUnlock(hData);
            }
        }
        finally
        {
            CloseClipboard(); // GetClipboardData 的内存归剪贴板所有，只解锁不释放
        }
    }

    /// <summary>写入纯文本；失败时 reason 给出失败步骤与 Win32 错误码（供诊断日志）。</summary>
    public static bool TrySetText(string text, out string reason)
    {
        reason = string.Empty;
        if (!OpenClipboard(IntPtr.Zero))
        {
            reason = $"OpenClipboard 失败 err={Marshal.GetLastWin32Error()}";
            return false;
        }

        var hMem = IntPtr.Zero;
        var handedOver = false; // 成功后内存归系统所有，不能再释放
        try
        {
            if (!EmptyClipboard())
            {
                reason = $"EmptyClipboard 失败 err={Marshal.GetLastWin32Error()}";
                return false;
            }

            // (len+1)*2：UTF-16 含终止符；GlobalAlloc 不保证清零，终止符须显式写
            hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)((text.Length + 1) * 2));
            if (hMem == IntPtr.Zero)
            {
                reason = $"GlobalAlloc 失败 err={Marshal.GetLastWin32Error()}";
                return false;
            }

            var target = GlobalLock(hMem);
            if (target == IntPtr.Zero)
            {
                reason = $"GlobalLock 失败 err={Marshal.GetLastWin32Error()}";
                return false;
            }

            Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
            Marshal.WriteInt16(target, text.Length * 2, 0);
            GlobalUnlock(hMem);

            if (SetClipboardData(CF_UNICODETEXT, hMem) == IntPtr.Zero)
            {
                reason = $"SetClipboardData 失败 err={Marshal.GetLastWin32Error()}";
                return false;
            }

            handedOver = true;
            return true;
        }
        finally
        {
            if (!handedOver && hMem != IntPtr.Zero)
            {
                GlobalFree(hMem);
            }

            CloseClipboard();
        }
    }

    /// <summary>清空剪贴板（语义同 Clipboard.Clear）。</summary>
    public static bool TryClear(out string reason)
    {
        reason = string.Empty;
        if (!OpenClipboard(IntPtr.Zero))
        {
            reason = $"OpenClipboard 失败 err={Marshal.GetLastWin32Error()}";
            return false;
        }

        try
        {
            if (!EmptyClipboard())
            {
                reason = $"EmptyClipboard 失败 err={Marshal.GetLastWin32Error()}";
                return false;
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);
}
