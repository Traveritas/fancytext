namespace FancyText.Core;

/// <summary>
/// 声明式步骤可引用的内置资源：命名映射表（<see cref="UseMapStep"/>）与命名算法（<see cref="AlgorithmStep"/>）。
/// 内置样式借此共享程序化生成的映射表（省内存），外部样式包借此调用编码 / Zalgo 等算法。
/// </summary>
internal static class KnownTransforms
{
    // ---------- 命名映射表 ----------

    public static IReadOnlyDictionary<char, string> ResolveMap(string name) => name switch
    {
        "bold" => LatinMaps.Bold,
        "italic" => LatinMaps.Italic,
        "bold-italic" => LatinMaps.BoldItalic,
        "script" => LatinMaps.Script,
        "bold-script" => LatinMaps.BoldScript,
        "fraktur" => LatinMaps.Fraktur,
        "bold-fraktur" => LatinMaps.BoldFraktur,
        "double-struck" => LatinMaps.DoubleStruck,
        "monospace" => LatinMaps.Monospace,
        "sans" => LatinMaps.Sans,
        "sans-bold" => LatinMaps.SansBold,
        "sans-italic" => LatinMaps.SansItalic,
        "sans-bold-italic" => LatinMaps.SansBoldItalic,
        "circled" => LatinMaps.Circled,
        "circled-negative" => LatinMaps.CircledNegative,
        "squared" => LatinMaps.Squared,
        "squared-negative" => LatinMaps.SquaredNegative,
        "parenthesized" => LatinMaps.Parenthesized,
        "regional-indicator" => LatinMaps.RegionalIndicator,
        "fullwidth" => LatinMaps.Fullwidth,
        "small-caps" => LatinMaps.SmallCaps,
        "superscript" => LatinMaps.Superscript,
        "subscript" => LatinMaps.Subscript,
        "currency" => LatinMaps.Currency,
        "symbols-mix" => LatinMaps.SymbolsMix,
        "leet" => LatinMaps.Leet,
        "upside-down" => LatinMaps.UpsideDown,
        "mirror" => LatinMaps.Mirror,
        "morse" => LatinMaps.Morse,
        "braille" => LatinMaps.Braille,
        "digits-circled-sans" => CircledSansDigits,
        "digits-double-circled" => DoubleCircledDigits,
        "cyrillic-lookalike" => CyrillicLookalike,
        "martian" => MartianDictionary.Map,
        "martian-reverse" => MartianReverse,
        _ => throw new ArgumentException($"未知内置映射表：{name}", nameof(name)),
    };

    // ---------- 数字变体表（原 StyleCatalog.Expanded，调研报告 2.A6） ----------

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

    // ---------- 俄化表（原 StyleCatalog.Expanded） ----------

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

    // ---------- 火星文反向字典（原 StyleCatalog.Expanded） ----------

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

    // ---------- 命名算法 ----------

    public static string ApplyAlgorithm(KnownAlgorithm algorithm, string input) => algorithm switch
    {
        KnownAlgorithm.ZalgoMini => ZalgoTransformer.Transform(input, ZalgoIntensity.Mini),
        KnownAlgorithm.ZalgoNormal => ZalgoTransformer.Transform(input, ZalgoIntensity.Normal),
        KnownAlgorithm.ZalgoMax => ZalgoTransformer.Transform(input, ZalgoIntensity.Max),
        KnownAlgorithm.Base64 => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(input)),
        KnownAlgorithm.Rot13 => LatinMaps.Rot13(input),
        KnownAlgorithm.Morse => LatinMaps.ToMorse(input),
        KnownAlgorithm.Braille => LatinMaps.ToBraille(input),
        KnownAlgorithm.Nato => EncodingTransforms.ToNato(input),
        KnownAlgorithm.A1Z26 => EncodingTransforms.ToA1Z26(input),
        KnownAlgorithm.Binary => EncodingTransforms.ToBinary(input),
        KnownAlgorithm.Hex => EncodingTransforms.ToHex(input),
        KnownAlgorithm.Uppercase => input.ToUpperInvariant(),
        KnownAlgorithm.Lowercase => input.ToLowerInvariant(),
        KnownAlgorithm.AlternatingCase => EncodingTransforms.ToAlternatingCase(input),
        KnownAlgorithm.StripCombiningMarks => TextTransforms.StripCombiningMarks(input),
        _ => input,
    };
}
