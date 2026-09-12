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
}
