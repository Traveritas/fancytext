using System.Runtime.InteropServices;

namespace FancyText.Desktop.Helpers;

/// <summary>桌面版用到的 Win32 互操作：全局热键注册 + 托盘图标句柄回收。</summary>
internal static class NativeMethods
{
    // RegisterHotKey 的修饰键位掩码（fsModifiers）
    public const uint MOD_ALT = 0x1;
    public const uint MOD_CONTROL = 0x2;
    public const uint MOD_SHIFT = 0x4;
    public const uint MOD_WIN = 0x8;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>NotifyIcon 图标由 bitmap.GetHicon() 创建，句柄不归 Icon 对象管，需自毁防泄漏。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // ---------- 按显示器取 DPI（弹窗定位需要鼠标所在显示器的缩放比） ----------

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint flags); // MONITOR_DEFAULTTONEAREST = 2

    /// <summary>MDT_EFFECTIVE_DPI = 0；Win8.1+。返回 0 表示成功。</summary>
    [DllImport("shcore.dll")]
    public static extern uint GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT pt);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern IntPtr FindWindow(string? className, string? windowName);

    /// <summary>Win11 圆角等窗口属性（DWMWA_WINDOW_CORNER_PREFERENCE=33）。返回 HRESULT，0=成功。</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>DWM 边框矩形（DwmExtendFrameIntoClientArea 用）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int Left, Right, Top, Bottom;
    }

    /// <summary>把窗口框架（玻璃/材质区）扩展进客户区：全 -1 = 整个客户区。无边框窗口（NCCALCSIZE 0）
    /// 上 SYSTEMBACKDROP_TYPE 能返回成功但什么都不画，必须先有"框架"区域材质才有地方落笔。</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    // ---------- 工作集修剪（轻量化：隐藏后把常驻内存观感从 ~117MB 压到 10-25MB） ----------

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess(); // 伪句柄，无需关闭

    /// <summary>把当前进程工作集整页换出。唤醒时靠软缺页换回（首次唤出慢几十 ms），
    /// 故只在隐藏 1.5s 后调用，且期间再次唤出会取消。</summary>
    [DllImport("psapi.dll")]
    public static extern uint EmptyWorkingSet(IntPtr hProcess);
}
