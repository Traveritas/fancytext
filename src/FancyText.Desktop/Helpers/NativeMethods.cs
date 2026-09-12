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
}
