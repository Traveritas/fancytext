using System.Globalization;

namespace FancyText.Core;

/// <summary>
/// 样式包输出膨胀估算：按步骤粗算「每个输入字符最多变成多少 UTF-16 码元」。
/// 只用于提示（超过 <see cref="WarnThreshold"/> 时警告，不拒装）；真正的兜底是运行时的 <see cref="MaxPackOutputChars"/> 截断。
/// 内置样式里膨胀最大的是二进制（约 27 倍），阈值留出余量，正常包不会触发。
/// </summary>
internal static class PackGrowth
{
    public const double WarnThreshold = 64;

    /// <summary>包样式单步输出上限：超出部分截断，防止坏包把每次按键的预览拖成卡死/OOM。正常使用远达不到。</summary>
    public const int MaxPackOutputChars = 1_000_000;

    public static IEnumerable<string> Warnings(StylePack pack)
    {
        foreach (var style in pack.Styles)
        {
            var factor = Estimate(style.Steps);
            if (factor > WarnThreshold)
            {
                var shown = factor >= 1e6 ? "百万" : Math.Round(factor).ToString(CultureInfo.InvariantCulture);
                yield return $"[{style.Id}] 每个字可能膨胀到约 {shown} 个字符，长文本转换可能卡顿";
            }
        }
    }

    /// <summary>每个输入字符的输出码元数上界（粗估）。</summary>
    public static double Estimate(IReadOnlyList<TransformStep> steps)
    {
        double length = 1, graphemes = 1; // 每个输入字符当前对应的码元数 / 字素数
        foreach (var step in steps)
        {
            Apply(step, ref length, ref graphemes);
        }

        return length;
    }

    private static void Apply(TransformStep step, ref double length, ref double graphemes)
    {
        switch (step)
        {
            case MapReplaceStep map:
                ApplyMap(map.Map, ref length, ref graphemes);
                break;
            case UseMapStep useMap:
                ApplyMap(KnownTransforms.ResolveMap(useMap.MapName), ref length, ref graphemes);
                break;
            case AppendMarkStep mark:
                length += graphemes * mark.Mark.Length * mark.Repeat;
                break;
            case WrapEachStep wrapEach:
                length += graphemes * (wrapEach.Prefix.Length + wrapEach.Suffix.Length);
                graphemes += graphemes * (Graphemes(wrapEach.Prefix) + Graphemes(wrapEach.Suffix));
                break;
            case SpacingStep spacing:
                length += graphemes * spacing.Separator.Length;
                graphemes += graphemes * Graphemes(spacing.Separator);
                break;
            case AlgorithmStep algorithm:
                var factor = AlgorithmFactor(algorithm.Algorithm);
                length *= factor.Length;
                graphemes = factor.ResetGraphemes ? length : graphemes;
                break;
            case IfChangedStep guard:
                Apply(guard.Inner, ref length, ref graphemes);
                break;

            // WrapString 只加常数长度，Reverse 不改长度
        }
    }

    private static void ApplyMap(IReadOnlyDictionary<char, string> map, ref double length, ref double graphemes)
    {
        int maxLength = 1, maxGraphemes = 1;
        foreach (var value in map.Values)
        {
            maxLength = Math.Max(maxLength, value.Length);
            maxGraphemes = Math.Max(maxGraphemes, Graphemes(value));
        }

        length *= maxLength;
        graphemes *= maxGraphemes;
    }

    private static int Graphemes(string text) => new StringInfo(text).LengthInTextElements;

    /// <summary>算法的长度倍数（按一个码元最多 3 个 UTF-8 字节计）；编码类输出逐字可见，字素数随长度重置。</summary>
    private static (double Length, bool ResetGraphemes) AlgorithmFactor(KnownAlgorithm algorithm) => algorithm switch
    {
        KnownAlgorithm.Binary => (27, true),        // 3 字节 ×「8 位 + 空格」
        KnownAlgorithm.Hex => (9, true),            // 3 字节 ×「2 位 + 空格」
        KnownAlgorithm.Base64 => (4, true),
        KnownAlgorithm.Morse => (8, true),
        KnownAlgorithm.Nato => (10, true),
        KnownAlgorithm.A1Z26 => (3, true),
        KnownAlgorithm.Pinyin => (7, true),
        KnownAlgorithm.ZalgoMini => (3, false),
        KnownAlgorithm.ZalgoNormal => (6, false),
        KnownAlgorithm.ZalgoMax => (14, false),
        _ => (1, false),
    };
}
