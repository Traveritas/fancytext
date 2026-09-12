using System.Text;

namespace FancyText.Core;

public enum ZalgoIntensity
{
    /// <summary>轻度：每字上下各最多 1 个组合符。</summary>
    Mini,

    /// <summary>中度：上下各最多 2 个、贯穿 1 个。</summary>
    Normal,

    /// <summary>重度：上下各最多 5 个、贯穿 3 个。</summary>
    Max,
}

/// <summary>
/// 魔鬼文字 / Zalgo / 诅咒文字：每字符随机叠加「上方/贯穿/下方」三组组合附加符号。
/// 码点池与 MelnikovIG/Zalgo（C#）和 Im-Rises/zalgo-generator（TS）两个开源实现一致，全在 U+0300–036F + U+0489。
/// </summary>
public static class ZalgoTransformer
{
    private static readonly string[] Up =
    [
        "\u0300", "\u0301", "\u0302", "\u0303", "\u0304", "\u0305", "\u0306", "\u0307", "\u0308", "\u0309",
        "\u030A", "\u030B", "\u030C", "\u030D", "\u030E", "\u030F", "\u0310", "\u0311", "\u0312", "\u0313",
        "\u0314", "\u031A", "\u033D", "\u033E", "\u033F", "\u0342", "\u0343", "\u0344", "\u0346", "\u034A",
        "\u034B", "\u034C", "\u0350", "\u0351", "\u0352", "\u035B", "\u0357",
        "\u0363", "\u0364", "\u0365", "\u0366", "\u0367", "\u0368", "\u0369", "\u036A", "\u036B", "\u036C",
        "\u036D", "\u036E", "\u036F",
    ];

    private static readonly string[] Mid =
    [
        "\u0315", "\u031B", "\u0321", "\u0322", "\u0327", "\u0328", "\u0334", "\u0335", "\u0336", "\u0337",
        "\u0338", "\u0340", "\u0341", "\u034F", "\u0358", "\u035C", "\u035D", "\u035E", "\u035F", "\u0360",
        "\u0361", "\u0362", "\u0489",
    ];

    private static readonly string[] Down =
    [
        "\u0316", "\u0317", "\u0318", "\u0319", "\u031C", "\u031D", "\u031E", "\u031F", "\u0320", "\u0323",
        "\u0324", "\u0325", "\u0326", "\u0329", "\u032A", "\u032B", "\u032C", "\u032D", "\u032E", "\u032F",
        "\u0330", "\u0331", "\u0332", "\u0333", "\u0339", "\u033A", "\u033B", "\u033C", "\u0345", "\u0347",
        "\u0348", "\u0349", "\u034D", "\u034E", "\u0353", "\u0354", "\u0355", "\u0356", "\u0359", "\u035A",
    ];

    public static string Transform(string input, ZalgoIntensity intensity, Random? random = null)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        random ??= Random.Shared;
        var (minUp, maxUp, minMid, maxMid, minDown, maxDown) = intensity switch
        {
            ZalgoIntensity.Mini => (1, 1, 0, 0, 1, 1),
            ZalgoIntensity.Normal => (1, 2, 1, 1, 1, 2),
            ZalgoIntensity.Max => (2, 5, 1, 3, 2, 5),
            _ => (1, 2, 1, 1, 1, 2),
        };

        var sb = new StringBuilder(input.Length * 4);
        foreach (var element in TextTransforms.EnumerateTextElements(input))
        {
            sb.Append(element);
            AppendRandomMarks(sb, Up, minUp, maxUp, random);
            AppendRandomMarks(sb, Mid, minMid, maxMid, random);
            AppendRandomMarks(sb, Down, minDown, maxDown, random);
        }

        return sb.ToString();
    }

    /// <summary>还原：去掉所有组合附加符号。对其它组合符样式（菊花体、删除线等）同样有效。</summary>
    public static string Strip(string input) => TextTransforms.StripCombiningMarks(input);

    private static void AppendRandomMarks(StringBuilder sb, string[] pool, int min, int max, Random random)
    {
        if (max <= 0 || pool.Length == 0)
        {
            return;
        }

        var count = min >= max ? min : random.Next(min, max + 1);
        for (var i = 0; i < count; i++)
        {
            sb.Append(pool[random.Next(pool.Length)]);
        }
    }
}
