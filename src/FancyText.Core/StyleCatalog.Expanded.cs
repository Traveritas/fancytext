using System.Globalization;
using System.Text;

namespace FancyText.Core;

/// <summary>
/// <see cref="StyleCatalog"/> 的第二批扩充样式：
/// 装饰模板（2.C1/2.C3）、组合符预设（2.B2）、数字变体（2.A6）、编码（2.E）、火星文还原（2.A5 反向）、俄化体。
/// 码点均取自 docs/01-调研报告-花式文字插件.md 对应小节的实测数据。
/// 组合附加符号一律写 \uXXXX 转义；模板中易混淆的藏文标记亦用转义，常见花符号沿用 wing- 系列的字面量写法。
/// </summary>
public static partial class StyleCatalog
{
    private static partial void AddExpandedStyles(List<TextStyle> list)
    {
        // ================= 组合符预设（调研报告 2.B2，中英通吃） =================

        list.Add(new()
        {
            Id = "paren-marks", Name = "连弧文 ͜͡", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u035C\u0361"),
            Note = "每字后附加 U+035C（组合双短音符下）+ U+0361（组合双倒弧），渲染成括号夹字",
        });
        list.Add(new()
        {
            Id = "stripes", Name = "横条纹字 ͟͞", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u035E\u035F"),
            Note = "每字后附加 U+035E（组合双长音符上）+ U+035F（组合双长音符下），上下夹出条纹",
        });
        list.Add(new()
        {
            Id = "lightning", Name = "闪电文 ͛", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u035B"),
            Note = "每字后附加 U+035B（组合闪电形符号，coolsymbol \"Lightning/Zigzag\" 预设）",
        });
        list.Add(new()
        {
            Id = "wave-head", Name = "角头字 ̚", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u031A"),
            Note = "每字后附加 U+031A（组合左角号上，调研报告 2.B2「波浪头」）",
        });
        list.Add(new()
        {
            Id = "x-above", Name = "叉上字 ̽", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u033D"),
            Note = "每字后附加 U+033D（组合叉号上）",
        });
        list.Add(new()
        {
            Id = "dot-below", Name = "点下字 ̣", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0323"),
            Note = "每字后附加 U+0323（组合下加点，调研报告 2.B2「点下」）",
        });
        list.Add(new()
        {
            Id = "manipuri-underline", Name = "曼尼普尔下划线 ꯭", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\uAADF"),
            Note = "每字后附加 U+AADF（曼尼普尔文组合标记，形似下划线）",
        });
        list.Add(new()
        {
            Id = "lace", Name = "花边文 ஊ", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0B8A"),
            Note = "每字后附加 U+0B8A（印度系文字区字符 ஊ，调研报告 2.B5「花边文」）",
        });
        list.Add(new()
        {
            Id = "grass", Name = "草头文 ෴", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0DF4"),
            Note = "每字后附加 U+0DF4（僧伽罗文 Kunḍaliya 花饰符 ෴）",
        });
        list.Add(new()
        {
            Id = "smoke-arabic", Name = "烟雾文 ❸", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u06E3", repeat: 7),
            Note = "每字后附加阿拉伯文小型低形符 U+06E3 × 7 堆叠（调研报告 2.B4「冒烟文③」）",
        });

        // ================= 装饰模板（调研报告 2.C1 翅膀/括号对 + 2.C3 边框） =================

        list.Add(new()
        {
            Id = "wing-elegant", Name = "典雅翅膀 ꧁❦༺", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "꧁❦༺", "༻❦꧂"),
            Note = "爪哇文括号 ꧁꧂ + 花形心符 ❦ U+2766 + 藏文花括号 ༺༻（2.C1「典雅」模板）",
        });
        list.Add(new()
        {
            Id = "wing-diamond", Name = "钻石翅膀 ꧁༺◇", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "꧁༺◇", "◇༻꧂"),
            Note = "白菱形 ◇ U+25C7 点缀（2.C1「钻石」模板，「霸气体」变体）",
        });
        list.Add(new()
        {
            Id = "wing-double", Name = "双层翅膀 ꧁༺༒", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "꧁༺\u0F12", "\u0F12༻꧂"),
            Note = "藏文 RDEL NAG RDEL DKAR ༒ U+0F12 点缀（2.C1「双层」模板）",
        });
        list.Add(new()
        {
            Id = "wing-shimmer", Name = "流光翅膀 ꧁༺✦", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "꧁༺✦", "✦༻꧂"),
            Note = "黑四角星 ✦ U+2726 点缀（2.C1「流光」模板）",
        });
        list.Add(new()
        {
            Id = "wing-soft", Name = "柔光翅膀 ꧁༺❁", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "꧁༺❁", "❁༻꧂"),
            Note = "重型花饰符 ❁ U+2741 点缀（2.C1「柔光」模板）",
        });
        list.Add(new()
        {
            Id = "wing-sanskrit", Name = "藏文花结 ༺࿈", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "༺\u0FC8", "\u0FC8༻"),
            Note = "藏文符号却丹菩提座 ࿈ U+0FC8（2.C1 神秘符号系）",
        });
        list.Add(new()
        {
            Id = "wing-tibetan-ornament", Name = "藏式花纹 ༺ཌ༈", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "༺\u0F4C\u0F08", "\u0F08\u0F51༻"),
            Note = "藏文字母 ཌ U+0F4C、标记 ༈ U+0F08、ད U+0F51 组合的藏式花纹模板",
        });
        list.Add(new()
        {
            Id = "wing-charm", Name = "护符边框 ༄༊", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "\u0F04\u0F0A", "\u0F7C\u0F82\u0F7E\u0FC6\u0FD0"),
            Note = "藏文起始符 ༄ U+0F04 + 信笺符 ༊ U+0F0A；后缀 ་元音/声调/花饰串 ོྂཾ࿆࿐（U+0F7C/82/7E/FC6/FD0）",
        });
        list.Add(new()
        {
            Id = "wing-heart", Name = "甜心括号 ෆ", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "ෆ", "ෆ"),
            Note = "僧伽罗文修饰符 ෆ U+0DC6，形似甜心括号（2.C1 可爱/古风系）",
        });
        list.Add(new()
        {
            Id = "wing-bunny", Name = "兔系装饰 ₍ᐢ", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "₍ᐢ", "ᐢ₎"),
            Note = "下标括号 ₍ U+208D/₎ U+208E + 加拿大原住民音节 ᐢ U+1422（2.C1 可爱系颜文字模板）",
        });
        list.Add(new()
        {
            Id = "wing-moonlight", Name = "月华装饰 ☾༺", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "☾༺", "༻☽"),
            Note = "上弦月 ☾ U+263E / 下弦月 ☽ U+263D + 藏文花括号 ༺༻（2.C1 模板）",
        });
        list.Add(new()
        {
            Id = "wing-starmoon", Name = "星月装饰 ☾✦", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "☾✦", "✦☽"),
            Note = "月牙 U+263E/U+263D + 黑四角星 ✦ U+2726（2.C1 模板）",
        });
        list.Add(new()
        {
            Id = "wing-asterism", Name = "星点装饰 ⁂", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "⁂", "⁂"),
            Note = "三星符 ⁂ U+2042（2.C1 模板）",
        });
        list.Add(new()
        {
            Id = "wing-retro", Name = "复古花纹 ꧁༺༽༾ཊ", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "꧁༺\u0F3D\u0F3E\u0F4A", "\u0F4F\u0F3F\u0F3C༻꧂"),
            Note = "藏文括号组 ༼ U+0F3C/༽ U+0F3D/༾ U+0F3E/༿ U+0F3F 与字母 ཊ U+0F4A/ཏ U+0F4F 拼成的复古花纹",
        });
        list.Add(new()
        {
            Id = "wing-dots", Name = "圆点边框 ｡o○", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "｡o○", "○o｡"),
            Note = "半角句号 ｡ U+FF61 + 白圆 ○ U+25CB 组成的日系边框（2.C1 模板）",
        });
        list.Add(new()
        {
            Id = "wing-nested", Name = "双层括号 ༼༺", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "\u0F3C༺", "༻\u0F3D"),
            Note = "藏文括号 ༼ U+0F3C / ༽ U+0F3D 内嵌花括号 ༺༻（2.C1 模板）",
        });
        list.Add(new()
        {
            Id = "wing-triangle", Name = "三角翅膀 ꧁༺△", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "꧁༺△", "△༻꧂"),
            Note = "白上三角 △ U+25B3 点缀（2.C1 模板）",
        });
        list.Add(new()
        {
            Id = "wing-love", Name = "爱心边框 •°•❤", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "•°•❤•°•", "•°•❤•°•"),
            Note = "• U+2022 ° U+00B0 ❤ U+2764 组成的 Love 边框（2.C3 模板）",
        });

        // ================= 数字专用变体（调研报告 2.A6，仅数字表，其余回退原字） =================

        list.Add(new()
        {
            Id = "digits-circled-sans", Name = "无衬线圈数字 ➀", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, CircledSansDigits),
            Note = "1–9 → ➀–➈（Dingbat 无衬线圈数字 U+2780–2788；区段共 1–10 十个字形、无 0，0 与其它字符回退原字）",
        });
        list.Add(new()
        {
            Id = "digits-double-circled", Name = "双圈数字 ⓵", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, DoubleCircledDigits),
            Note = "1–9 → ⓵–⓽（双圈数字 U+24F5–24FD；区段共 1–10 十个字形、无 0，0 与其它字符回退原字）",
        });

        // ================= 俄化（形近西里尔字母，LatinFancy） =================

        list.Add(new()
        {
            Id = "cyrillic-lookalike", Name = "俄化体（西里尔形近）", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, CyrillicLookalike),
            Note = "形近西里尔大写替换：A→А B→В E→Е K→К M→М H→Н O→О P→Р C→С T→Т X→Х Y→У（U+0410 区）",
        });

        // ================= 火星文还原（Chinese，反向字典） =================

        list.Add(new()
        {
            Id = "martian-reverse", Name = "火星文还原", Category = TextStyleCategory.Chinese,
            Transform = s => TextTransforms.MapReplace(s, MartianReverse),
            Note = "由 MartianDictionary 反向构建（火星文 → 简体；一对多时取第一个，非 BMP 字形跳过）",
        });

        // ================= 编码（调研报告 2.E） =================

        list.Add(new()
        {
            Id = "braille", Name = "盲文", Category = TextStyleCategory.Encoding,
            Transform = LatinMaps.ToBraille,
            Note = "grade-1 逐字母：a–z 按 8 点盲文点位映射（a=⠁ U+2801 起），大小写同形；数字串前加数字符 ⠼ U+283C，1–9/0 复用 a–j 形",
        });
        list.Add(new()
        {
            Id = "nato", Name = "NATO 字母表", Category = TextStyleCategory.Encoding,
            Transform = ToNato,
            Note = "字母 → ICAO/NATO 音标单词（Alfa Bravo … Zulu），大小写同形，非字母忽略",
        });
        list.Add(new()
        {
            Id = "a1z26", Name = "A1Z26", Category = TextStyleCategory.Encoding,
            Transform = ToA1Z26,
            Note = "a=1 … z=26：字母间用连字符、词间用空格（字母密码）",
        });
        list.Add(new()
        {
            Id = "binary", Name = "二进制", Category = TextStyleCategory.Encoding,
            Transform = ToBinary,
            Note = "UTF-8 字节 → 每字节 8 位二进制，空格分隔（ASCII 字符即每字符 8 位）",
        });
        list.Add(new()
        {
            Id = "hex", Name = "十六进制", Category = TextStyleCategory.Encoding,
            Transform = ToHex,
            Note = "UTF-8 字节 → 每字节 2 位大写十六进制，空格分隔（ASCII 字符即每字符 2 位）",
        });

        // ================= 变换补充（评审建议的标配三项） =================

        list.Add(new()
        {
            Id = "uppercase", Name = "全大写 ABC", Category = TextStyleCategory.Transform,
            Transform = s => s.ToUpperInvariant(),
            Note = "全部字母转大写（不变文化规则）",
        });
        list.Add(new()
        {
            Id = "lowercase", Name = "全小写 abc", Category = TextStyleCategory.Transform,
            Transform = s => s.ToLowerInvariant(),
            Note = "全部字母转小写（不变文化规则）",
        });
        list.Add(new()
        {
            Id = "alternating-case", Name = "交替大小写 aLt", Category = TextStyleCategory.Transform,
            Transform = ToAlternatingCase,
            Note = "大小写逐字交替（sPoNgE cAsE），跳过非字母不计相位",
        });
    }

    // ---------- 数字变体表 ----------

    private static IReadOnlyDictionary<char, string>? _circledSansDigits;
    private static IReadOnlyDictionary<char, string> CircledSansDigits => _circledSansDigits ??= BuildDigitMap(0x2780);

    private static IReadOnlyDictionary<char, string>? _doubleCircledDigits;
    private static IReadOnlyDictionary<char, string> DoubleCircledDigits => _doubleCircledDigits ??= BuildDigitMap(0x24F5);

    private static IReadOnlyDictionary<char, string> BuildDigitMap(int start)
    {
        var map = new Dictionary<char, string>(9);
        for (var i = 1; i <= 9; i++)
        {
            map[(char)('0' + i)] = char.ConvertFromUtf32(start + i - 1);
        }

        return map;
    }

    // ---------- 俄化表 ----------

    private static IReadOnlyDictionary<char, string>? _cyrillicLookalike;
    private static IReadOnlyDictionary<char, string> CyrillicLookalike => _cyrillicLookalike ??= new Dictionary<char, string>
    {
        ['A'] = "\u0410", // А
        ['B'] = "\u0412", // В
        ['E'] = "\u0415", // Е
        ['K'] = "\u041A", // К
        ['M'] = "\u041C", // М
        ['H'] = "\u041D", // Н
        ['O'] = "\u041E", // О
        ['P'] = "\u0420", // Р
        ['C'] = "\u0421", // С
        ['T'] = "\u0422", // Т
        ['X'] = "\u0425", // Х
        ['Y'] = "\u0423", // У
    };

    // ---------- 火星文反向字典 ----------

    private static IReadOnlyDictionary<char, string>? _martianReverse;
    private static IReadOnlyDictionary<char, string> MartianReverse => _martianReverse ??= BuildMartianReverse();

    private static IReadOnlyDictionary<char, string> BuildMartianReverse()
    {
        var reverse = new Dictionary<char, string>(MartianDictionary.Map.Count);
        foreach (var (simple, spark) in MartianDictionary.Map)
        {
            // 仅单码元（BMP 单字素）的火星文字形可作键；同一火星字对应多个简体时保留先遍历到的一个
            if (spark.Length == 1 && !reverse.ContainsKey(spark[0]))
            {
                reverse[spark[0]] = simple.ToString();
            }
        }

        return reverse;
    }

    // ---------- 编码辅助 ----------

    private static readonly string[] NatoWords =
    {
        "Alfa", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel", "India",
        "Juliett", "Kilo", "Lima", "Mike", "November", "Oscar", "Papa", "Quebec", "Romeo",
        "Sierra", "Tango", "Uniform", "Victor", "Whiskey", "X-ray", "Yankee", "Zulu",
    };

    private static string ToNato(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var words = new List<string>();
        foreach (var ch in input)
        {
            var lower = char.ToLowerInvariant(ch);
            if (lower is >= 'a' and <= 'z')
            {
                words.Add(NatoWords[lower - 'a']);
            }
        }

        return string.Join(' ', words);
    }

    private static string ToA1Z26(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var words = new List<string>();
        foreach (var word in input.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var numbers = new List<string>();
            foreach (var ch in word)
            {
                var lower = char.ToLowerInvariant(ch);
                if (lower is >= 'a' and <= 'z')
                {
                    numbers.Add((lower - 'a' + 1).ToString(CultureInfo.InvariantCulture));
                }
            }

            if (numbers.Count > 0)
            {
                words.Add(string.Join('-', numbers));
            }
        }

        return string.Join(' ', words);
    }

    private static string ToBinary(string input) =>
        string.IsNullOrEmpty(input)
            ? input
            : string.Join(' ', Encoding.UTF8.GetBytes(input).Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));

    private static string ToHex(string input) =>
        string.IsNullOrEmpty(input)
            ? input
            : string.Join(' ', Encoding.UTF8.GetBytes(input).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

    private static string ToAlternatingCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var sb = new StringBuilder(input.Length);
        var upper = false;
        foreach (var ch in input)
        {
            if (!char.IsLetter(ch))
            {
                sb.Append(ch);
                continue;
            }

            sb.Append(upper ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch));
            upper = !upper;
        }

        return sb.ToString();
    }
}
