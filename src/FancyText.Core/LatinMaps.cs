using System.Text;

namespace FancyText.Core;

/// <summary>
/// 拉丁花体映射表。数学字母数字符号区段（U+1D400–1D7FF）用「区段起点 + 洞字符例外表」程序化生成；
/// 其余（小型大写、上下标、倒转、镜像、货币体、符号体等）用字面量对照表。
/// 码点依据 Unicode 官方 Mathematical Alphanumeric Symbols 区段表、fileformat.info 倒转表、smalltextgen 镜像表。
/// </summary>
public static class LatinMaps
{
    // ---------- 数学字母数字符号（区段起点 + 洞字符，程序化生成） ----------

    public static IReadOnlyDictionary<char, string> Bold { get; } =
        BuildBlock(upperStart: 0x1D400, lowerStart: 0x1D41A, digitStart: 0x1D7CE);

    public static IReadOnlyDictionary<char, string> Italic { get; } =
        BuildBlock(upperStart: 0x1D434, lowerStart: 0x1D44E, overrides: new Dictionary<char, string>
        {
            ['h'] = "\u210E", // ℎ：U+1D455 为未分配码位，洞字符在 Letterlike Symbols 区
        });

    public static IReadOnlyDictionary<char, string> BoldItalic { get; } =
        BuildBlock(upperStart: 0x1D468, lowerStart: 0x1D482);

    /// <summary>Script 手写体。大写 B/E/F/H/I/L/M/R 与小写 e/g/o 是「洞」字符，位于 Letterlike Symbols 区。</summary>
    public static IReadOnlyDictionary<char, string> Script { get; } =
        BuildBlock(upperStart: 0x1D49C, lowerStart: 0x1D4B6, overrides: new Dictionary<char, string>
        {
            ['B'] = "\u212C", ['E'] = "\u2130", ['F'] = "\u2131", ['H'] = "\u210B",
            ['I'] = "\u2110", ['L'] = "\u2112", ['M'] = "\u2133", ['R'] = "\u211B",
            ['e'] = "\u212F", ['g'] = "\u210A", ['o'] = "\u2134",
        });

    public static IReadOnlyDictionary<char, string> BoldScript { get; } =
        BuildBlock(upperStart: 0x1D4D0, lowerStart: 0x1D4EA);

    /// <summary>Fraktur 哥特体。大写 C/H/I/R/Z 为洞字符。</summary>
    public static IReadOnlyDictionary<char, string> Fraktur { get; } =
        BuildBlock(upperStart: 0x1D504, lowerStart: 0x1D51E, overrides: new Dictionary<char, string>
        {
            ['C'] = "\u212D", ['H'] = "\u210C", ['I'] = "\u2111", ['R'] = "\u211C", ['Z'] = "\u2128",
        });

    public static IReadOnlyDictionary<char, string> BoldFraktur { get; } =
        BuildBlock(upperStart: 0x1D56C, lowerStart: 0x1D586);

    /// <summary>双线体（空心字）。大写 C/H/N/P/Q/R/Z 为洞字符，数字在独立区段。</summary>
    public static IReadOnlyDictionary<char, string> DoubleStruck { get; } =
        BuildBlock(upperStart: 0x1D538, lowerStart: 0x1D552, digitStart: 0x1D7D8, overrides: new Dictionary<char, string>
        {
            ['C'] = "\u2102", ['H'] = "\u210D", ['N'] = "\u2115", ['P'] = "\u2119",
            ['Q'] = "\u211A", ['R'] = "\u211D", ['Z'] = "\u2124", // R=ℝ：勿与花体 R ℜ U+211C（上方 Fraktur 表）混淆
        });

    public static IReadOnlyDictionary<char, string> Monospace { get; } =
        BuildBlock(upperStart: 0x1D670, lowerStart: 0x1D68A, digitStart: 0x1D7F6);

    public static IReadOnlyDictionary<char, string> Sans { get; } =
        BuildBlock(upperStart: 0x1D5A0, lowerStart: 0x1D5BA, digitStart: 0x1D7E2);

    public static IReadOnlyDictionary<char, string> SansBold { get; } =
        BuildBlock(upperStart: 0x1D5D4, lowerStart: 0x1D5EE, digitStart: 0x1D7EC);

    public static IReadOnlyDictionary<char, string> SansItalic { get; } =
        BuildBlock(upperStart: 0x1D608, lowerStart: 0x1D622);

    public static IReadOnlyDictionary<char, string> SansBoldItalic { get; } =
        BuildBlock(upperStart: 0x1D63C, lowerStart: 0x1D656);

    // ---------- 带圈 / 方框 / 括号 / 旗帜 ----------

    /// <summary>泡泡字（带圈字母）。数字为独立区段：0 = ⓪ U+24EA，1–9 = ①–⑨。</summary>
    public static IReadOnlyDictionary<char, string> Circled { get; } =
        BuildBlock(upperStart: 0x24B6, lowerStart: 0x24D0,
            overrides: FromPairs("0123456789", "⓪①②③④⑤⑥⑦⑧⑨"));

    /// <summary>黑底圈字 U+1F150–169，仅有大写；小写映射到同一字形。数字 1–9 = ❶–❾，0 = ⓿。</summary>
    public static IReadOnlyDictionary<char, string> CircledNegative { get; } =
        BuildBlock(upperStart: 0x1F150, lowerStart: 0x1F150,
            overrides: FromPairs("1234567890", "❶❷❸❹❺❻❼❽❾⓿"));

    /// <summary>方块字 U+1F130–149，仅有大写。</summary>
    public static IReadOnlyDictionary<char, string> Squared { get; } =
        BuildBlock(upperStart: 0x1F130, lowerStart: 0x1F130);

    /// <summary>黑底方块字 U+1F170–189，仅有大写。</summary>
    public static IReadOnlyDictionary<char, string> SquaredNegative { get; } =
        BuildBlock(upperStart: 0x1F170, lowerStart: 0x1F170);

    /// <summary>括号字：大写 U+1F110，小写 U+249C；数字 1–9 = ⑴–⑼。</summary>
    public static IReadOnlyDictionary<char, string> Parenthesized { get; } =
        BuildBlock(upperStart: 0x1F110, lowerStart: 0x249C,
            overrides: FromPairs("123456789", "⑴⑵⑶⑷⑸⑹⑺⑻⑼"));

    /// <summary>旗帜字母（Regional Indicator U+1F1E6–1F1FF），在 Discord 等平台可能显示为国旗。</summary>
    public static IReadOnlyDictionary<char, string> RegionalIndicator { get; } =
        BuildBlock(upperStart: 0x1F1E6, lowerStart: 0x1F1E6);

    // ---------- 全角 ----------

    /// <summary>全角/蒸汽波：ASCII 可见区 → +0xFEE0，空格 → 全角空格 U+3000。</summary>
    public static IReadOnlyDictionary<char, string> Fullwidth { get; } = BuildFullwidth();

    // ---------- 小型大写 / 上下标 ----------

    public static IReadOnlyDictionary<char, string> SmallCaps { get; } = FromPairs(
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ",
        "ᴀʙᴄᴅᴇꜰɢʜɪᴊᴋʟᴍɴᴏᴘǫʀꜱᴛᴜᴠᴡxʏᴢᴀʙᴄᴅᴇꜰɢʜɪᴊᴋʟᴍɴᴏᴘǫʀꜱᴛᴜᴠᴡxʏᴢ");

    /// <summary>上标。小写 q 无上标字符（回退原字）；大写缺 C/F/Q/S/X/Y/Z 的专用形，回退小写上标形。</summary>
    public static IReadOnlyDictionary<char, string> Superscript { get; } = FromPairs(
        "abcdefghijklmnopqrstuvwxyz" + "ABDEGHIJKLMNOPRTUVW" + "CFQSXYZ" + "0123456789",
        "ᵃᵇᶜᵈᵉᶠᵍʰⁱʲᵏˡᵐⁿᵒᵖqʳˢᵗᵘᵛʷˣʸᶻ" + "ᴬᴮᴰᴱᴳᴴᴵᴶᴷᴸᴹᴺᴼᴾᴿᵀᵁⱽᵂ" + "ᶜᶠqˢˣʸᶻ" + "⁰¹²³⁴⁵⁶⁷⁸⁹");

    /// <summary>下标。缺 b/c/d/f/g/q/w/y/z（不在表内即回退原字）；大写回退对应小写下标形。</summary>
    public static IReadOnlyDictionary<char, string> Subscript { get; } = FromPairs(
        "aehijklmnoprstuvx" + "AEHIJKLMNOPRSTUVX" + "0123456789",
        "ₐₑₕᵢⱼₖₗₘₙₒₚᵣₛₜᵤᵥₓ" + "ₐₑₕᵢⱼₖₗₘₙₒₚᵣₛₜᵤᵥₓ" + "₀₁₂₃₄₅₆₇₈₉");

    // ---------- 同形异码混排体 ----------

    /// <summary>货币体（instafonts/coolsymbol 对照表，J/Q/V 无货币字形，保留原字）。</summary>
    public static IReadOnlyDictionary<char, string> Currency { get; } = FromPairs(
        "abcdefghijklmnopqrstuvwxyz" + "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
        "₳฿₵ĐɆ₣₲ⱧłJ₭Ⱡ₥₦Ø₱QⱤ₴₮ɄV₩ӾɎⱫ" + "₳฿₵ĐɆ₣₲ⱧłJ₭Ⱡ₥₦Ø₱QⱤ₴₮ɄV₩ӾɎⱫ");

    /// <summary>符号体（coolsymbol "Symbols" 表：泰/希伯来/希腊/西里尔形近字混排）。</summary>
    public static IReadOnlyDictionary<char, string> SymbolsMix { get; } = FromPairs(
        "abcdefghijklmnopqrstuvwxyz" + "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
        "ค๒ς๔єғﻮђเןкɭ๓ห๏ρợгรтยשฬאчz" + "ค๒ς๔єғﻮђเןкɭ๓ห๏ρợгรтยשฬאчz");

    /// <summary>Leet 语（基础级）。</summary>
    public static IReadOnlyDictionary<char, string> Leet { get; } = FromPairs(
        "aeiostblgAEIOSTBLG",
        "431057819431057819");

    // ---------- 倒转 / 镜像（先映射，再整体倒序） ----------

    /// <summary>倒转文字映射表（fileformat.info），配合 <see cref="TextTransforms.MapAndReverse"/> 使用。</summary>
    public static IReadOnlyDictionary<char, string> UpsideDown { get; } = FromPairs(
        "abcdefghijklmnopqrstuvwxyz" + "ABCDEFGHIJKLMNOPQRSTUVWXYZ" + "0123456789" + ".,'\"?!()[]{}<>&_",
        "ɐqɔpǝɟƃɥᴉɾʞlɯuodbɹsʇnʌʍxʎz"
        + "∀𐐒ƆᗡƎℲ⅁HIſ⋊˥WNOԀΌᴚS⊥∩ΛMX⅄Z"
        + "0ƖᄅƐㄣϛ9ㄥ86"
        + "˙',„¿¡)(][}{><⅋‾");

    /// <summary>镜像文字映射表（smalltextgen/textreverse），配合 <see cref="TextTransforms.MapAndReverse"/> 使用。</summary>
    public static IReadOnlyDictionary<char, string> Mirror { get; } = FromPairs(
        "abcdefghijklmnopqrstuvwxyz" + "ABCDEFGHIJKLMNOPQRSTUVWXYZ" + "()[]{}<>&",
        "adɔbɘꟻǫʜiꞁʞlmᴎopqɿꙅtuvwxyz"
        + "AᙠƆᗡƎꟻӘHIႱ⋊⅃MИOꟼỌЯƧTUVWXYƸ"
        + ")(][}{><⅋");

    // ---------- 编码表 ----------

    /// <summary>摩斯电码表。</summary>
    public static IReadOnlyDictionary<char, string> Morse { get; } = new Dictionary<char, string>
    {
        ['A'] = ".-", ['B'] = "-...", ['C'] = "-.-.", ['D'] = "-..", ['E'] = ".",
        ['F'] = "..-.", ['G'] = "--.", ['H'] = "....", ['I'] = "..", ['J'] = ".---",
        ['K'] = "-.-", ['L'] = ".-..", ['M'] = "--", ['N'] = "-.", ['O'] = "---",
        ['P'] = ".--.", ['Q'] = "--.-", ['R'] = ".-.", ['S'] = "...", ['T'] = "-",
        ['U'] = "..-", ['V'] = "...-", ['W'] = ".--", ['X'] = "-..-", ['Y'] = "-.--",
        ['Z'] = "--..",
        ['0'] = "-----", ['1'] = ".----", ['2'] = "..---", ['3'] = "...--", ['4'] = "....-",
        ['5'] = ".....", ['6'] = "-....", ['7'] = "--...", ['8'] = "---..", ['9'] = "----.",
    };

    public static string ToMorse(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var sb = new StringBuilder();
        foreach (var word in input.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (sb.Length > 0)
            {
                sb.Append(" / ");
            }

            var first = true;
            foreach (var ch in word)
            {
                if (Morse.TryGetValue(char.ToUpperInvariant(ch), out var code))
                {
                    if (!first)
                    {
                        sb.Append(' ');
                    }

                    sb.Append(code);
                    first = false;
                }
            }
        }

        return sb.ToString();
    }

    public static string Rot13(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (ch is >= 'a' and <= 'z')
            {
                sb.Append((char)('a' + (ch - 'a' + 13) % 26));
            }
            else if (ch is >= 'A' and <= 'Z')
            {
                sb.Append((char)('A' + (ch - 'A' + 13) % 26));
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    // ---------- 盲文（grade-1） ----------

    /// <summary>
    /// 盲文 grade-1 字母表：a–z 映射到 8 点盲文区（U+2800 + 点位位掩码）。
    /// 点位：dot1=0x01 dot2=0x02 dot3=0x04 dot4=0x08 dot5=0x10 dot6=0x20。
    /// </summary>
    public static IReadOnlyDictionary<char, string> Braille { get; } = BuildBraille();

    /// <summary>
    /// 盲文 grade-1 逐字母编码：大小写同形；数字串前加数字符 ⠼（U+283C），
    /// 数字 1–9/0 复用 a–j 的点位字形（1=⠁ … 9=⠊ 0=⠚）。非字母数字原样保留。
    /// </summary>
    public static string ToBraille(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var sb = new StringBuilder(input.Length * 2);
        var inNumber = false;
        foreach (var ch in input)
        {
            if (ch is >= '0' and <= '9')
            {
                if (!inNumber)
                {
                    sb.Append('\u283C'); // 数字符 ⠼
                    inNumber = true;
                }

                var letter = ch == '0' ? 'j' : (char)('a' + (ch - '1'));
                sb.Append(Braille[letter]);
                continue;
            }

            inNumber = false;
            var lower = char.ToLowerInvariant(ch);
            if (lower is >= 'a' and <= 'z')
            {
                sb.Append(Braille[lower]);
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static IReadOnlyDictionary<char, string> BuildBraille()
    {
        // 标准 grade-1 点位：a–j 依次为 1/12/14/145/15/124/1245/125/24/245，
        // k–t 在 a–j 基础上加 dot3，u–z（除 w）再加 dot6，w = 2456。
        int[] dotMasks =
        {
            0x01, 0x03, 0x09, 0x19, 0x11, 0x0B, 0x1B, 0x13, 0x0A, 0x1A, // a–j
            0x05, 0x07, 0x0D, 0x1D, 0x15, 0x0F, 0x1F, 0x17, 0x0E, 0x1E, // k–t
            0x25, 0x27, 0x3A, 0x2D, 0x3D, 0x35,                         // u–z
        };

        var map = new Dictionary<char, string>(26);
        for (var i = 0; i < dotMasks.Length; i++)
        {
            map[(char)('a' + i)] = ((char)(0x2800 + dotMasks[i])).ToString();
        }

        return map;
    }

    // ---------- 构建辅助 ----------

    private static IReadOnlyDictionary<char, string> BuildBlock(
        int? upperStart,
        int? lowerStart,
        int? digitStart = null,
        IReadOnlyDictionary<char, string>? overrides = null)
    {
        var map = new Dictionary<char, string>(100);
        if (upperStart is { } u)
        {
            for (var i = 0; i < 26; i++)
            {
                map[(char)('A' + i)] = char.ConvertFromUtf32(u + i);
            }
        }

        if (lowerStart is { } l)
        {
            for (var i = 0; i < 26; i++)
            {
                map[(char)('a' + i)] = char.ConvertFromUtf32(l + i);
            }
        }

        if (digitStart is { } d)
        {
            for (var i = 0; i < 10; i++)
            {
                map[(char)('0' + i)] = char.ConvertFromUtf32(d + i);
            }
        }

        if (overrides != null)
        {
            foreach (var (key, value) in overrides)
            {
                map[key] = value;
            }
        }

        return map;
    }

    private static IReadOnlyDictionary<char, string> BuildFullwidth()
    {
        var map = new Dictionary<char, string>(100);
        for (var c = '\u0021'; c <= '\u007E'; c++)
        {
            map[c] = ((char)(c + 0xFEE0)).ToString();
        }

        map[' '] = "\u3000"; // 全角空格
        return map;
    }

    /// <summary>
    /// 从两条字符串按位置构建对照表。keys 必须全是 ASCII（BMP 单码元）；
    /// values 按 Unicode 标量（Rune）逐个配对，因此允许包含增补平面字符（如 𐐒）。
    /// </summary>
    private static IReadOnlyDictionary<char, string> FromPairs(string keys, string values)
    {
        var forms = new List<string>(values.Length);
        foreach (var rune in values.EnumerateRunes())
        {
            forms.Add(rune.ToString());
        }

        if (keys.Length != forms.Count)
        {
            throw new InvalidOperationException($"映射表长度不一致：keys={keys.Length}, values={forms.Count}");
        }

        var map = new Dictionary<char, string>(keys.Length);
        for (var i = 0; i < keys.Length; i++)
        {
            map[keys[i]] = forms[i];
        }

        return map;
    }
}
