using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace FancyText.Desktop.Helpers;

/// <summary>热键绑定：Win32 修饰键位掩码 + WPF 键枚举 + 原样显示文本。</summary>
internal readonly record struct HotkeyBinding(uint Modifiers, Key Key, string Display)
{
    public const string DefaultDisplay = "Ctrl+Alt+F";

    public static HotkeyBinding Default { get; } =
        DesktopConfig.Parse(DefaultDisplay) is { } binding ? binding : throw new InvalidOperationException("内置默认热键解析失败");
}

/// <summary>
/// 桌面版配置，存 %LOCALAPPDATA%\FancyText\desktop.json（与插件状态 state.json 同目录、不同文件）。
/// 目前仅一项：{"hotkey":"Ctrl+Alt+F"}。文件缺失/格式非法一律回退默认，不弹错——配置靠手改文件，读取必须宽容。
/// </summary>
internal static class DesktopConfig
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FancyText", "desktop.json");

    public static HotkeyBinding LoadHotkey()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
                if (doc.RootElement.TryGetProperty("hotkey", out var value) &&
                    value.ValueKind == JsonValueKind.String &&
                    Parse(value.GetString()) is { } binding)
                {
                    return binding;
                }
            }
        }
        catch (Exception)
        {
            // 读取/解析失败按默认热键处理
        }

        return HotkeyBinding.Default;
    }

    /// <summary>
    /// 解析 "Ctrl+Alt+Shift+键名"：修饰键可任意组合，键名用 Enum.TryParse&lt;Key&gt;（忽略大小写）。
    /// 无法解析返回 null，由调用方决定回退。
    /// </summary>
    public static HotkeyBinding? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        uint modifiers = 0;
        foreach (var token in parts[..^1])
        {
            if (!TryModifier(token, out var modifier))
            {
                return null; // 未知修饰键（如 "Cmd"）视为整体非法
            }

            modifiers |= modifier;
        }

        // 键名必须是具体键，不允许裸修饰键（如 "Ctrl+Shift"），否则会吞掉所有打字
        if (!Enum.TryParse(parts[^1], ignoreCase: true, out Key key) ||
            key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
        {
            return null;
        }

        return new HotkeyBinding(modifiers, key, text.Trim());
    }

    private static bool TryModifier(string token, out uint modifier)
    {
        switch (token.ToLowerInvariant())
        {
            case "ctrl":
            case "control":
                modifier = NativeMethods.MOD_CONTROL;
                return true;
            case "alt":
                modifier = NativeMethods.MOD_ALT;
                return true;
            case "shift":
                modifier = NativeMethods.MOD_SHIFT;
                return true;
            case "win":
            case "windows":
            case "meta":
                modifier = NativeMethods.MOD_WIN;
                return true;
            default:
                modifier = 0;
                return false;
        }
    }
}
