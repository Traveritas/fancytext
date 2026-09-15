using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace FancyText.Desktop.Helpers;

/// <summary>热键绑定：Win32 修饰键位掩码 + WPF 键枚举 + 原样显示文本。</summary>
internal readonly record struct HotkeyBinding(uint Modifiers, Key Key, string Display)
{
    public const string DefaultDisplay = "Ctrl+Alt+F";

    public static HotkeyBinding Default { get; } =
        DesktopSettings.ParseHotkey(DefaultDisplay) is { } binding ? binding : throw new InvalidOperationException("内置默认热键解析失败");
}

/// <summary>
/// 桌面版全部设置，存 %LOCALAPPDATA%\FancyText\desktop.json（与插件状态 state.json 同目录、不同文件）。
/// 设计为不可变 record：改任何一项都整体重存；所有项即时生效。
/// 文件缺失/格式非法一律回退默认，不弹错——读取必须宽容（含旧版 {"hotkey":"..."} 单键文件）。
/// </summary>
internal sealed record DesktopSettings
{
    public string Hotkey { get; init; } = HotkeyBinding.DefaultDisplay;
    public string Accent { get; init; } = "#6352DC";   // 强调色 #RRGGBB；"auto" = 跟随系统强调色
    public string Theme { get; init; } = "light";       // light | dark | system
    public string PreviewSize { get; init; } = "medium"; // small | medium | large
    public bool LaunchAtLogin { get; init; }
    public bool PrefillClipboard { get; init; } = true; // 唤出时预填剪贴板文字（关=保留上次输入）
    public bool PrefillSelection { get; init; } = true; // 唤出时优先预填其它应用中选中的文字（UIA 只读，失败回退剪贴板）
    public bool HideAfterCopy { get; init; } = true;    // 复制后收起弹窗（关=留在原地，状态栏提示已复制）
    public string PopupPosition { get; init; } = "cursor"; // cursor | primary

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>预览字号（pt）：由 PreviewSize 推导，不参与持久化。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double PreviewFontSize => PreviewSize switch
    {
        "small" => 14d,
        "large" => 18.5d,
        _ => 16d,
    };

    public static DesktopSettings Load()
    {
        try
        {
            var path = FilePath();
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return new DesktopSettings();
                }

                var s = new DesktopSettings();
                if (root.TryGetProperty("hotkey", out var hotkey) &&
                    hotkey.ValueKind == JsonValueKind.String &&
                    ParseHotkey(hotkey.GetString()) is not null)
                {
                    s = s with { Hotkey = hotkey.GetString()!.Trim() };
                }

                if (root.TryGetProperty("accent", out var accent) &&
                    accent.ValueKind == JsonValueKind.String)
                {
                    var text = accent.GetString()!;
                    if (text is "auto" || TryParseColor(text, out _, out _, out _))
                    {
                        s = s with { Accent = text.Trim().ToUpperInvariant() };
                    }
                }

                s = ReadString(root, s, "theme", v => v is "light" or "dark" or "system");
                s = ReadString(root, s, "previewSize", v => v is "small" or "medium" or "large");
                s = ReadString(root, s, "popupPosition", v => v is "cursor" or "primary");
                s = ReadBool(root, s, "launchAtLogin");
                s = ReadBool(root, s, "prefillClipboard");
                s = ReadBool(root, s, "prefillSelection");
                s = ReadBool(root, s, "hideAfterCopy");
                return s;
            }
        }
        catch (Exception)
        {
            // 读取/解析失败按默认设置处理
        }

        return new DesktopSettings();
    }

    public void Save()
    {
        try
        {
            var path = FilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
            // 保存失败不弹错：本次会话仍用内存值，下次启动回退上次成功的文件
        }
    }

    /// <summary>解析 #RRGGBB；失败返回 false（out 色不限）。</summary>
    public static bool TryParseColor(string? text, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var t = text.Trim().TrimStart('#');
        if (t.Length != 6)
        {
            return false;
        }

        return byte.TryParse(t[..2], System.Globalization.NumberStyles.HexNumber, null, out r)
            && byte.TryParse(t[2..4], System.Globalization.NumberStyles.HexNumber, null, out g)
            && byte.TryParse(t[4..6], System.Globalization.NumberStyles.HexNumber, null, out b);
    }

    /// <summary>
    /// 解析 "Ctrl+Alt+Shift+键名"：修饰键可任意组合，键名用 Enum.TryParse&lt;Key&gt;（忽略大小写）。
    /// 无法解析返回 null，由调用方决定回退。
    /// </summary>
    public static HotkeyBinding? ParseHotkey(string? text)
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

    private static string FilePath() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FancyText", "desktop.json");

    private static DesktopSettings ReadString(JsonElement root, DesktopSettings s, string name, Func<string, bool> validate)
    {
        if (root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
        {
            var text = v.GetString();
            if (text is not null && validate(text))
            {
                return name switch
                {
                    "theme" => s with { Theme = text },
                    "previewSize" => s with { PreviewSize = text },
                    "popupPosition" => s with { PopupPosition = text },
                    _ => s,
                };
            }
        }
        return s;
    }

    private static DesktopSettings ReadBool(JsonElement root, DesktopSettings s, string name)
    {
        if (root.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            var value = v.GetBoolean();
            return name switch
            {
                "launchAtLogin" => s with { LaunchAtLogin = value },
                "prefillClipboard" => s with { PrefillClipboard = value },
                "prefillSelection" => s with { PrefillSelection = value },
                "hideAfterCopy" => s with { HideAfterCopy = value },
                _ => s,
            };
        }
        return s;
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

/// <summary>兼容旧代码路径的入口（旧 API 只剩 LoadHotkey 语义，统一走 DesktopSettings）。</summary>
internal static class DesktopConfig
{
    public static HotkeyBinding LoadHotkey() =>
        DesktopSettings.ParseHotkey(DesktopSettings.Load().Hotkey) ?? HotkeyBinding.Default;
}
