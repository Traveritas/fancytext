using System.Globalization;
using System.Text;

namespace FancyText.Core;

/// <summary>
/// 编码/拼写类辅助变换（NATO、A1Z26、二进制、十六进制、交替大小写）。
/// 供 <see cref="KnownTransforms.ApplyAlgorithm"/> 调用，实现与历史版本逐行一致。
/// </summary>
internal static class EncodingTransforms
{
    private static readonly string[] NatoWords =
    {
        "Alfa", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel", "India",
        "Juliett", "Kilo", "Lima", "Mike", "November", "Oscar", "Papa", "Quebec", "Romeo",
        "Sierra", "Tango", "Uniform", "Victor", "Whiskey", "X-ray", "Yankee", "Zulu",
    };

    public static string ToNato(string input)
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

    public static string ToA1Z26(string input)
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

    public static string ToBinary(string input) =>
        string.IsNullOrEmpty(input)
            ? input
            : string.Join(' ', Encoding.UTF8.GetBytes(input).Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));

    public static string ToHex(string input) =>
        string.IsNullOrEmpty(input)
            ? input
            : string.Join(' ', Encoding.UTF8.GetBytes(input).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

    public static string ToAlternatingCase(string input)
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
