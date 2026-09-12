using System.Windows.Media;

namespace FancyText.Desktop.Helpers;

/// <summary>
/// 主题：由单一强调色 + 明/暗推导全套界面笔刷。
/// 浅色：淡背景 = 提亮+降饱和，悬停/选中 = 中等提亮；深色：暗底 = 降饱和压暗，悬停/选中 = 微微提亮，
/// 强调色过暗时自动提亮保证对比度。换色只改 Accent，全套观感一致跟随。
/// </summary>
internal sealed class Theme
{
    public static Theme DefaultLight { get; } = new(Color.FromRgb(0x63, 0x52, 0xDC), dark: false);

    public Theme(Color accent, bool dark)
    {
        Dark = dark;
        var (h, s, _) = ToHsl(accent);

        // 深色底上过暗的强调色（如深蓝/近黑）自动提亮到可读亮度，色相饱和度保留
        var (_, accentS, accentL) = ToHsl(accent);
        if (dark && accentL < 0.55)
        {
            var (ar, ag, ab) = FromHsl(h, Math.Min(accentS, 0.75), 0.62);
            accent = Color.FromRgb(ar, ag, ab);
        }

        Accent = accent;
        Primary = Freeze(accent);

        if (dark)
        {
            WindowBackground = Tint(h, s, 0.155, 0.250);
            Text = FreezeRgb(0xEC, 0xEA, 0xF4);          // 正文：近白，不随强调色
            Meta = FreezeRgb(0x9C, 0x9A, 0xB0);          // 次要说明文字：中性灰
            KeycapText = FreezeRgb(0xC7, 0xC5, 0xD8);
            InputIdleBackground = Tint(h, s, 0.215, 0.220);
            InputFocusBackground = Tint(h, s, 0.245, 0.280);
            ChipBackground = Tint(h, s, 0.235, 0.180);
            HoverChip = Tint(h, s, 0.300, 0.450);
            HoverItem = Tint(h, s, 0.280, 0.350);
            SelectedItem = Tint(h, s, 0.320, 0.420);
            WindowBorder = Tint(h, s, 0.320, 0.150);
            Separator = Tint(h, s, 0.220, 0.100);
            ScrollThumb = Tint(h, s, 0.400, 0.250);
            KeycapBorder = Tint(h, s, 0.360, 0.350);
            KeycapBackground = Tint(h, s, 0.255, 0.200);
        }
        else
        {
            WindowBackground = FreezeRgb(0xFB, 0xFB, 0xFE);
            Text = FreezeRgb(0x25, 0x24, 0x33);          // 正文：近黑，不随强调色
            Meta = FreezeRgb(0x77, 0x75, 0x8A);          // 次要说明文字：中性灰
            KeycapText = FreezeRgb(0x50, 0x4E, 0x68);
            InputIdleBackground = FreezeRgb(0xF1, 0xF0, 0xF8);
            InputFocusBackground = FreezeRgb(0xFF, 0xFF, 0xFF);
            ChipBackground = Tint(h, s, 0.930, 0.060);   // 分类胶囊底：几乎中性
            HoverChip = Tint(h, s, 0.900, 0.450);        // 胶囊悬停
            HoverItem = Tint(h, s, 0.953, 0.350);        // 列表项悬停
            SelectedItem = Tint(h, s, 0.925, 0.420);     // 列表项选中
            WindowBorder = Tint(h, s, 0.935, 0.100);     // 窗口/输入框描边
            Separator = Tint(h, s, 0.960, 0.050);        // 分隔线
            ScrollThumb = Tint(h, s, 0.850, 0.250);      // 滚动条
            KeycapBorder = Tint(h, s, 0.800, 0.350);     // 键帽描边
            KeycapBackground = Tint(h, s, 0.945, 0.200); // 键帽底
        }
    }

    public bool Dark { get; }
    public Color Accent { get; }
    public Brush Primary { get; }
    public Brush WindowBackground { get; }
    public Brush Text { get; }
    public Brush Meta { get; }
    public Brush InputIdleBackground { get; }
    public Brush InputFocusBackground { get; }
    public Brush ChipBackground { get; }
    public Brush HoverChip { get; }
    public Brush HoverItem { get; }
    public Brush SelectedItem { get; }
    public Brush WindowBorder { get; }
    public Brush Separator { get; }
    public Brush ScrollThumb { get; }
    public Brush KeycapBorder { get; }
    public Brush KeycapBackground { get; }
    public Brush KeycapText { get; }

    /// <summary>滚动条拇指色（XAML 模板里只能塞字符串）。</summary>
    public string ScrollThumbHex => $"#{ScrollThumbColor.R:X2}{ScrollThumbColor.G:X2}{ScrollThumbColor.B:X2}";

    private Color ScrollThumbColor => ((SolidColorBrush)ScrollThumb).Color;

    private static Brush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Brush FreezeRgb(byte r, byte g, byte b) => Freeze(Color.FromRgb(r, g, b));

    /// <summary>以色相/饱和度为基准，给定亮度与"保留的饱和度比例"调出色板色。</summary>
    private static Brush Tint(double h, double s, double lightness, double saturationScale)
    {
        var (r, g, b) = FromHsl(h, s * saturationScale, lightness);
        return FreezeRgb(r, g, b);
    }

    /// <summary>RGB(0-255) → HSL(h:0-360, s/l:0-1)。</summary>
    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255d, g = c.G / 255d, b = c.B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2;
        if (max == min)
        {
            return (0, 0, l);
        }

        double d = max - min;
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r
            ? ((g - b) / d + (g < b ? 6 : 0)) * 60
            : max == g ? ((b - r) / d + 2) * 60 : ((r - g) / d + 4) * 60;
        return (h, s, l);
    }

    private static (byte R, byte G, byte B) FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs((h / 60 % 2) - 1));
        double m = l - c / 2;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return ((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
