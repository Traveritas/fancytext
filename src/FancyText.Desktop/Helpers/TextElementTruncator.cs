using System.Globalization;

namespace FancyText.Desktop.Helpers;

/// <summary>字素截断工具（预览只转换前 N 个字素）。</summary>
internal static class TextElementTruncator
{
    /// <summary>
    /// 按扩展字素簇截断（emoji ZWJ 序列、旗帜、肤色修饰、韩文音节、CRLF 都算一个）。
    /// 增量扫描、凑满即停——代价只与结果长度成正比，与输入全文长度无关。
    /// </summary>
    public static string Truncate(string? text, int maxElements)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxElements)
        {
            return text ?? string.Empty; // UTF-16 长度不超限时字素数必然不超限
        }

        var elements = 0;
        var i = 0;
        while (i < text.Length)
        {
            i += StringInfo.GetNextTextElementLength(text.AsSpan(i));
            elements++;
            if (elements == maxElements)
            {
                return i >= text.Length ? text : text[..i];
            }
        }

        return text;
    }
}
