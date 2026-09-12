using System.Runtime.InteropServices;

namespace FancyText.Desktop.Helpers;

/// <summary>
/// 组合符码点 → 覆盖字体的动态解析器（普适方案，替代逐区间硬编码）。
///
/// 背景：WPF 把"基字+组合符"整簇锁死在基字字体（Script=Inherited 的码点继承基字 script，
/// 永不回退），挂 CJK 基字必豆腐。此前按码点区间硬编码归宿字体，两轮补丁后仍出新漏网
/// （本机实测 U+20DD 硬编码表都判错；"Sans Serif Collection" 这类兜底字体覆盖冷门码点）。
///
/// 方案（调研实测：建库 180ms、+1MB 堆、查询 0.3μs/码点）：GDI EnumFontFamiliesEx 枚举
/// 字体面 → CreateFont+SelectObject 进复用 DC → GetFontUnicodeRanges 拿每字体的
/// BMP 覆盖区间表 → Resolve(码点) 按"优先字体序→枚举序"找第一个覆盖它的字体。
/// 只对"组合符白名单"（Unicode 16 Script=Inherited ∪ 实证豆腐段）里的码点生效，
/// 其余码点（自带 script 的藏/泰/南亚符号等）交还 WPF 自身的切分回退——它们本就正常。
/// </summary>
internal static class FontCoverage
{
    /// <summary>
    /// 需要拆 Run 的码点白名单 = Unicode 16 Inherited（Scripts.txt）∪ 实证豆腐段。
    /// 0483–0489、A670 段在 Unicode 13+ 已改判 Cyrillic，但 WPF 内置 script 表停在旧版
    /// 仍当 Inherited 锁簇（FontProbe 实证），故并入；2CEF/A674/FE2E 一并预防性纳入。
    /// SMP 码点（E0100 变体符等）样式未用且 GDI 表只覆盖 BMP，不做解析。
    /// </summary>
    private static readonly (ushort Lo, ushort Hi)[] MarkRanges =
    [
        (0x0300, 0x036F), // 组合附加符号（Zalgo 池/删除线/下划线等）
        (0x0483, 0x0489), // 西里尔组合符（菊花圈）
        (0x064B, 0x0655), (0x0670, 0x0670), // 阿拉伯 Quranic 音符（烟雾文）
        (0x0951, 0x0954),
        (0x1AB0, 0x1ACE), (0x1CD0, 0x1CD2), (0x1CD4, 0x1CE0), (0x1CE2, 0x1CE8),
        (0x1CED, 0x1CED), (0x1CF4, 0x1CF4), (0x1CF8, 0x1CF9),
        (0x1DC0, 0x1DFF),
        (0x200C, 0x200D), // ZWJ/ZWNJ（default-ignorable，拆分无害）
        (0x20D0, 0x20F0), // 组合记号（顶箭头/包围圈/三角/雪花等）
        (0x2CEF, 0x2CF1),
        (0x302A, 0x302D), (0x3099, 0x309A),
        (0xA66F, 0xA67D), // 西里尔扩展组合符
        (0xFE00, 0xFE0F), (0xFE20, 0xFE2F), // 变体选择符/半记号（多为 default-ignorable）
    ];

    /// <summary>解析命中时的字体优先序：先符号字体后正文字体，兜底字体（Sans Serif Collection）天然靠后。</summary>
    private static readonly string[] PriorityFaces =
    [
        "Segoe UI Symbol", "Arial", "Calibri", "Cambria", "Segoe UI", "Segoe UI Historic",
        "Nirmala UI", "Ebrima", "Microsoft YaHei UI", "Microsoft JhengHei UI",
        "Meiryo", "MS Gothic", "Leelawadee UI", "Microsoft Himalaya", "Tahoma", "Times New Roman",
    ];

    private sealed record FontFace(string Name, (ushort Lo, ushort Hi)[] Ranges);

    private static readonly object Gate = new();
    private static volatile FontFace[]? _faces;        // 优先序排列的覆盖表
    private static readonly Dictionary<int, string?> Cache = []; // 码点 → 字体名（null=留基链）

    /// <summary>码点是否属于组合符白名单（拆 Run 的前提）。</summary>
    internal static bool IsMark(int cp)
    {
        foreach (var (lo, hi) in MarkRanges)
        {
            if (cp >= lo && cp <= hi)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 解析组合符码点的归宿字体名；null = 非组合符或无字体覆盖（交还 WPF 基链）。
    /// 结果缓存；首次调用触发建库（约 200ms，建议启动后台 Warmup 预热）。
    /// </summary>
    internal static string? Resolve(int cp)
    {
        if (Cache.TryGetValue(cp, out var cached))
        {
            return cached;
        }

        var faces = _faces ?? Build();
        string? hit = null;
        if (IsMark(cp))
        {
            foreach (var face in faces)
            {
                if (Covers(face, cp))
                {
                    hit = face.Name;
                    break;
                }
            }
        }

        lock (Gate)
        {
            Cache[cp] = hit;
        }

        return hit;
    }

    /// <summary>启动后台预热（App 启动时调用，用户首次唤出前建好覆盖表）。</summary>
    internal static void Warmup() => _ = Build();

    private static bool Covers(FontFace face, int cp)
    {
        foreach (var (lo, hi) in face.Ranges)
        {
            if (cp >= lo && cp <= hi)
            {
                return true;
            }

            if (lo > cp)
            {
                break; // 区间按升序，越过即无
            }
        }

        return false;
    }

    // ================================================== GDI 覆盖表构建 ==================================================

    private static FontFace[] Build()
    {
        lock (Gate)
        {
            if (_faces is { } built)
            {
                return built;
            }

            _faces = BuildCore() ?? [];
            return _faces;
        }
    }

    private static FontFace[]? BuildCore()
    {
        try
        {
            var names = EnumerateFaces();
            var priority = new List<FontFace>();
            var rest = new List<FontFace>();

            IntPtr hdc = IntPtr.Zero;
            try
            {
                hdc = GetDC(IntPtr.Zero);
                if (hdc == IntPtr.Zero)
                {
                    return null;
                }

                foreach (var name in names)
                {
                    var ranges = RangesForFace(hdc, name);
                    if (ranges is not { Length: > 0 })
                    {
                        continue;
                    }

                    var face = new FontFace(name, ranges);
                    var index = Array.IndexOf(PriorityFaces, name);
                    if (index >= 0)
                    {
                        priority.Add(face);
                    }
                    else
                    {
                        rest.Add(face);
                    }
                }
            }
            finally
            {
                if (hdc != IntPtr.Zero)
                {
                    ReleaseDC(IntPtr.Zero, hdc);
                }
            }

            priority.Sort((a, b) => Array.IndexOf(PriorityFaces, a.Name).CompareTo(Array.IndexOf(PriorityFaces, b.Name)));
            return [.. priority, .. rest];
        }
        catch (Exception)
        {
            return null; // GDI 异常（无桌面会话等）：解析器整体退化为"全部留基链"，不致崩
        }
    }

    private static List<string> EnumerateFaces()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var callback = new EnumFontFamExProc((ref ENUMLOGFONTEXW elf, IntPtr _, uint type, IntPtr _) =>
        {
            if ((type & (RASTER_FONTTYPE | DEVICE_FONTTYPE)) != 0)
            {
                return 1; // 位图/设备字体的覆盖声明不可信
            }

            seen.Add(elf.elfLogFont.lfFaceName.ToString());
            return 1;
        });

        var hdc = GetDC(IntPtr.Zero);
        try
        {
            var lf = new LOGFONTW
            {
                lfCharSet = DEFAULT_CHARSET, // 全字符集枚举
                lfFaceName = string.Empty,
            };
            EnumFontFamiliesExW(hdc, ref lf, callback, IntPtr.Zero, 0);
        }
        finally
        {
            if (hdc != IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        return [.. seen];
    }

    /// <summary>单字体 BMP 覆盖区间（GetFontUnicodeRanges，区间升序）。</summary>
    private static (ushort, ushort)[]? RangesForFace(IntPtr hdc, string faceName)
    {
        var lf = new LOGFONTW
        {
            lfCharSet = DEFAULT_CHARSET,
            lfFaceName = faceName,
        };
        var hFont = CreateFontIndirectW(ref lf);
        if (hFont == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var old = SelectObject(hdc, hFont);
            if (old == IntPtr.Zero)
            {
                return null;
            }

            var size = GetFontUnicodeRanges(hdc, IntPtr.Zero);
            if (size == 0)
            {
                return null;
            }

            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetFontUnicodeRanges(hdc, buffer) != size)
                {
                    return null;
                }

                // GLYPHSET: {DWORD cbThis; DWORD flAccel; DWORD cGlyphsSupported; DWORD cRanges; WCRANGE ranges[]}
                var count = Marshal.ReadInt32(buffer, 12);
                var ranges = new (ushort, ushort)[count];
                for (var i = 0; i < count; i++)
                {
                    var low = (ushort)Marshal.ReadInt16(buffer, 16 + i * 4);
                    var span = (ushort)Marshal.ReadInt16(buffer, 16 + i * 4 + 2);
                    ranges[i] = (low, (ushort)(low + span - 1));
                }

                return ranges;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            _ = SelectObject(hdc, GetStockObject(SYSTEM_FONT)); // 恢复旧对象前先解选自建字体
            DeleteObject(hFont);
        }
    }

    // ================================================== Win32 ==================================================

    private const uint RASTER_FONTTYPE = 1;
    private const uint DEVICE_FONTTYPE = 2;
    private const byte DEFAULT_CHARSET = 1;
    private const int SYSTEM_FONT = 13;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LOGFONTW
    {
        public int lfHeight;
        public int lfWidth;
        public int lfEscapement;
        public int lfOrientation;
        public int lfWeight;
        public byte lfItalic;
        public byte lfUnderline;
        public byte lfStrikeOut;
        public byte lfCharSet;
        public byte lfOutPrecision;
        public byte lfClipPrecision;
        public byte lfQuality;
        public byte lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string lfFaceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ENUMLOGFONTEXW
    {
        public LOGFONTW elfLogFont;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string elfFullName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string elfStyle;
    }

    private delegate int EnumFontFamExProc(ref ENUMLOGFONTEXW lpelfe, IntPtr lpntme, uint fontType, IntPtr lParam);

    // GetDC/ReleaseDC 在 user32（不是 gdi32）
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int EnumFontFamiliesExW(IntPtr hdc, ref LOGFONTW logfont, EnumFontFamExProc proc, IntPtr lParam, uint dwFlags);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontIndirectW(ref LOGFONTW logfont);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int fnObject);

    [DllImport("gdi32.dll")]
    private static extern uint GetFontUnicodeRanges(IntPtr hdc, IntPtr lpgs);
}
