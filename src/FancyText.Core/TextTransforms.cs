using System.Globalization;
using System.Text;

namespace FancyText.Core;

/// <summary>
/// 四个正交转换原语 + 若干辅助变换。
/// 全部样式（见 <see cref="StyleCatalog"/>）都由这些原语组合而成：
/// ① <see cref="MapReplace"/> 查表映射；② <see cref="AppendMark"/> 组合附加符号叠加；
/// ③ <see cref="WrapString"/>/<see cref="WrapEach"/> 装饰插入；④ 拼写/方向变换（<see cref="Reverse"/> 等）。
/// </summary>
public static class TextTransforms
{
    /// <summary>原语①：逐字符查表替换（值为 string 以支持增补平面字符，如 𝐀 🅐）。</summary>
    public static string MapReplace(string input, IReadOnlyDictionary<char, string> map)
    {
        if (string.IsNullOrEmpty(input) || map.Count == 0)
        {
            return input;
        }

        var sb = new StringBuilder(input.Length * 2);
        foreach (var ch in input)
        {
            if (map.TryGetValue(ch, out var replacement))
            {
                sb.Append(replacement);
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 原语②：每个字素（text element）后追加组合附加符号串，渲染时叠加在前一个字上。
    /// 中英文通用；<paramref name="repeat"/> 用于堆叠出冒烟等效果。
    /// </summary>
    public static string AppendMark(string input, string mark, int repeat = 1)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var sb = new StringBuilder(input.Length * (1 + mark.Length * repeat));
        foreach (var element in EnumerateTextElements(input))
        {
            sb.Append(element);
            for (var i = 0; i < repeat; i++)
            {
                sb.Append(mark);
            }
        }

        return sb.ToString();
    }

    /// <summary>原语③a：整串前后包围（翅膀、边框、括号模板）。</summary>
    public static string WrapString(string input, string prefix, string suffix) => prefix + input + suffix;

    /// <summary>原语③b：逐字包围（每个字素前后都插装饰）。</summary>
    public static string WrapEach(string input, string charPrefix, string charSuffix)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var sb = new StringBuilder(input.Length * (1 + charPrefix.Length + charSuffix.Length));
        foreach (var element in EnumerateTextElements(input))
        {
            sb.Append(charPrefix).Append(element).Append(charSuffix);
        }

        return sb.ToString();
    }

    /// <summary>字素间插入分隔符（aesthetic 分字、宽体）。</summary>
    public static string Spacing(string input, string separator)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        return string.Join(separator, EnumerateTextElements(input));
    }

    /// <summary>按字素（而非 UTF-16 码元）倒序，避免拆散代理对和组合符序列。</summary>
    public static string Reverse(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var elements = EnumerateTextElements(input);
        elements.Reverse();
        return string.Concat(elements);
    }

    /// <summary>
    /// 先查表映射再倒序（倒转/镜像文字的实现方式）。
    /// 若映射零命中（如纯中文之于拉丁倒转表），返回原文——由上层"输出==输入即不适用"的过滤逻辑隐藏，
    /// 避免退化成普通倒序却顶着"倒转文字"的名字误导用户。
    /// </summary>
    public static string MapAndReverse(string input, IReadOnlyDictionary<char, string> map)
    {
        var mapped = MapReplace(input, map);
        return string.Equals(mapped, input, StringComparison.Ordinal) ? input : Reverse(mapped);
    }

    /// <summary>
    /// 去掉本引擎会生成的组合附加符号（U+0300–U+036F、U+0483–U+0489、藏文/泰文等装饰码点），
    /// 用于"还原/清洗"已装饰的文本。
    /// </summary>
    public static string StripCombiningMarks(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (!IsDecorativeCombiningMark(ch))
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    internal static bool IsDecorativeCombiningMark(char c) =>
        (c >= '\u0300' && c <= '\u036F') ||  // 拉丁组合附加符号（含 Zalgo、各类线条）
        (c >= '\u0483' && c <= '\u0489') ||  // 西里尔组合符（菊花体 ҈ ҉）
        (c >= '\u0F71' && c <= '\u0F87') ||  // 藏文元音/声调组合符（飞鸟、蝴蝶、冒烟）
        (c >= '\u0E31' && c <= '\u0E3A' && c != '\u0E32' && c != '\u0E33') || // 泰文组合符（萌芽 ็ ้）
        (c >= '\u0E47' && c <= '\u0E4E') ||  // 泰文声调组合符
        (c >= '\u0EC8' && c <= '\u0ECD') ||  // 老挝文组合符
        c == '\uA9BF' ||                     // 爪哇文组合元音符 ꦿ
        c == '\u1B44' ||                     // 巴厘文组合符 ᭄
        (c >= '\u20D0' && c <= '\u20F0') ||  // 组合记号用符号（圈/框/三角包围）
        c == '\uAADF' ||                     // 曼尼普尔组合符
        c == '\u0749';                       // 叙利亚文缩写符

    /// <summary>按字素枚举（代理对、组合符序列保持完整）。</summary>
    public static List<string> EnumerateTextElements(string input)
    {
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(input);
        while (enumerator.MoveNext())
        {
            elements.Add((string)enumerator.Current!);
        }

        return elements;
    }
}
