using System.Globalization;

namespace FancyText.Desktop.Helpers;

/// <summary>
/// 字素截断工具。实现复制自 CmdPal 的 DebouncedTextPageBase.TruncateTextElements
/// （刻意不改 Core：两个宿主各自维护，避免为 UI 细节污染引擎）。
/// </summary>
internal static class TextElementTruncator
{
    /// <summary>按字素截断。增量扫描、凑满即停——代价只与结果长度成正比，与输入全文长度无关。</summary>
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
            // 基本字符：代理对按一个字素处理
            if (char.IsHighSurrogate(text[i]))
            {
                i += i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
            }
            else
            {
                i++;
            }

            // 粘住后续组合记号（组成一个完整字素）
            while (i < text.Length && IsCombining(text[i]))
            {
                i++;
            }

            elements++;
            if (elements == maxElements)
            {
                return i >= text.Length ? text : text[..i];
            }
        }

        return text;
    }

    private static bool IsCombining(char c)
    {
        var category = char.GetUnicodeCategory(c);
        return category == UnicodeCategory.NonSpacingMark
            || category == UnicodeCategory.SpacingCombiningMark
            || category == UnicodeCategory.EnclosingMark;
    }
}
