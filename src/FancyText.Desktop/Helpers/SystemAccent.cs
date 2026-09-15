using System.Runtime.InteropServices;

namespace FancyText.Desktop.Helpers;

/// <summary>
/// Windows 系统强调色（设置 → 个性化 → 颜色；含"从壁纸自动取色"的结果）。
/// DWM colorization 即任务栏/开始菜单实际着色，P/Invoke 系统 dwmapi.dll，零依赖。
/// </summary>
internal static class SystemAccent
{
    /// <summary>读当前系统强调色；DWM 不可用（极罕见）返回 null 由调用方回退默认紫。</summary>
    public static (byte R, byte G, byte B)? TryGet()
    {
        try
        {
            if (DwmGetColorizationColor(out var colorization, out _) != 0)
            {
                return null;
            }

            // 返回 0xAABBGGRR（注意 BGR 序），仅取不透明色
            return ((byte)(colorization >> 16 & 0xFF), (byte)(colorization >> 8 & 0xFF), (byte)(colorization & 0xFF));
        }
        catch (Exception)
        {
            return null; // dwmapi 加载失败等（无 DWM 的环境）
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetColorizationColor(out uint colorization, out bool opaqueBlend);
}
