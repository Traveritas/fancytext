using System.IO.Compression;
using System.Text;

namespace FancyText.Core;

/// <summary>
/// 中文文本转换：简⇄繁（OpenCC 字典，词级贪心最长匹配解决"头发/發"一类一对多）与拼音（mozillazg/pinyin-data）。
/// 字典为 gzip 嵌入资源，惰性加载——首次使用对应样式时才解压构建映射，不用不占内存。
/// </summary>
internal static class ChineseText
{
    private const string StResource = "FancyText.Core.Resources.opencc-st.txt.gz"; // 简→繁
    private const string TsResource = "FancyText.Core.Resources.opencc-ts.txt.gz"; // 繁→简
    private const string PinyinResource = "FancyText.Core.Resources.pinyin.txt.gz";

    private static DictionaryTextMap? _simplifiedToTraditional;
    private static DictionaryTextMap? _traditionalToSimplified;
    private static IReadOnlyDictionary<char, string>? _pinyin;

    /// <summary>简体 → 繁体（词级最长匹配；OpenCC STCharacters + STPhrases）。</summary>
    public static string ToTraditional(string input) =>
        (_simplifiedToTraditional ??= LoadGreedyMap(StResource)).Convert(input);

    /// <summary>繁体 → 简体（词级最长匹配；OpenCC TSCharacters + TSPhrases）。</summary>
    public static string ToSimplified(string input) =>
        (_traditionalToSimplified ??= LoadGreedyMap(TsResource)).Convert(input);

    /// <summary>汉字 → 带声调拼音（词间空格）；多音字取最常用读音，非汉字原样保留。</summary>
    public static string ToPinyin(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var map = _pinyin ??= LoadPinyin();
        var parts = new List<string>();
        var buffer = new StringBuilder();
        foreach (var ch in input)
        {
            if (map.TryGetValue(ch, out var syllable))
            {
                if (buffer.Length > 0)
                {
                    parts.Add(buffer.ToString());
                    buffer.Clear();
                }

                parts.Add(syllable);
            }
            else
            {
                buffer.Append(ch); // 非汉字（或字典未收录）原样保留，相邻归为一"词"
            }
        }

        if (buffer.Length > 0)
        {
            parts.Add(buffer.ToString());
        }

        return string.Join(' ', parts);
    }

    /// <summary>汉字 → 拼音首字母（网络缩写文体：你好吗 → nhm）；英文/数字保留。</summary>
    public static string ToPinyinAbbr(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var map = _pinyin ??= LoadPinyin();
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (map.TryGetValue(ch, out var syllable) && syllable.Length > 0)
            {
                sb.Append(char.ToLowerInvariant(syllable[0]));
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    // ---------- 字典加载 ----------

    /// <summary>
    /// 词级贪心最长匹配映射：文件行格式「键\t值」（多候选以空格分隔，取第一个）。
    /// 两个来源文件合并时字表在前、词表在后（后加载覆盖同键），词级条目优先级自然更高。
    /// </summary>
    private static DictionaryTextMap LoadGreedyMap(string resourceName)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var maxKeyLength = 1;
        foreach (var line in ReadLines(resourceName))
        {
            var tab = line.IndexOf('\t');
            if (tab <= 0 || tab == line.Length - 1)
            {
                continue;
            }

            var key = line[..tab];
            var values = line[(tab + 1)..].Split(' ');
            map[key] = values[0];
            if (key.Length > maxKeyLength)
            {
                maxKeyLength = key.Length;
            }
        }

        return new DictionaryTextMap(map, maxKeyLength);
    }

    /// <summary>行格式「U+XXXX: pīn,yīn  # 字」——多音字取第一个（最常用）读音。</summary>
    private static IReadOnlyDictionary<char, string> LoadPinyin()
    {
        var map = new Dictionary<char, string>(24_000);
        foreach (var line in ReadLines(PinyinResource))
        {
            // 行格式「U+XXXX: pīn,yīn  # 字」——"U+XXXX"（含 U+ 前缀共 6 字符）后紧跟冒号
            var colon = line.IndexOf(':');
            if (colon != 6 || !line.StartsWith("U+", StringComparison.Ordinal))
            {
                continue;
            }

            var hex = line[2..colon];
            if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var codePoint)
                || codePoint is < char.MinValue or > char.MaxValue)
            {
                continue;
            }

            var rest = line[(colon + 1)..].TrimStart();
            var hash = rest.IndexOf('#');
            if (hash >= 0)
            {
                rest = rest[..hash];
            }

            var first = rest.Split(',')[0].Trim();
            if (first.Length > 0)
            {
                map.TryAdd((char)codePoint, first); // 并行源里先出现的（常用）优先
            }
        }

        return map;
    }

    private static IEnumerable<string> ReadLines(string resourceName)
    {
        var assembly = typeof(ChineseText).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"嵌入资源缺失：{resourceName}");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && line[0] is not '#' and not '\ufeff')
            {
                yield return line;
            }
        }
    }

    /// <summary>贪心最长匹配转换器：从当前位置向左取最长候选词查表，命中即转换，未命中原样保留。</summary>
    private sealed class DictionaryTextMap(Dictionary<string, string> map, int maxKeyLength)
    {
        public string Convert(string input)
        {
            if (string.IsNullOrEmpty(input) || map.Count == 0)
            {
                return input;
            }

            var sb = new StringBuilder(input.Length);
            for (var i = 0; i < input.Length;)
            {
                var matched = false;
                // 最长优先：词级条目（"头发→頭髮"）先于单字兜底（"发→發"）
                for (var length = Math.Min(maxKeyLength, input.Length - i); length > 1 && !matched; length--)
                {
                    if (i + length > input.Length)
                    {
                        continue;
                    }

                    if (map.TryGetValue(input.Substring(i, length), out var replacement))
                    {
                        sb.Append(replacement);
                        i += length;
                        matched = true;
                    }
                }

                if (!matched)
                {
                    if (map.TryGetValue(input[i..(i + 1)], out var single))
                    {
                        sb.Append(single);
                    }
                    else
                    {
                        sb.Append(input[i]);
                    }

                    i++;
                }
            }

            return sb.ToString();
        }
    }
}
