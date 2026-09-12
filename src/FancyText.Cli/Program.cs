using System.Text;
using System.Text.Json;
using FancyText.Core;

namespace FancyText.Cli;

/// <summary>
/// 花式文字独立命令行工具（引擎与 CmdPal 插件共用 FancyText.Core）。
/// 用法：
///   fancy                     对示例文本列出全部样式
///   fancy <文本>              对该文本列出全部样式
///   fancy --list              列出样式 ID / 名称 / 分类
///   fancy <样式ID> <文本>     单样式转换，只输出结果（适合脚本管道）
///   fancy --random [文本]     随机挑一个样式
///   fancy --json [文本]       以 JSON 输出全部转换结果
///   fancy --demo              同 <文本>，别名
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var rest = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();

        if (args.Contains("--list"))
        {
            foreach (var g in StyleCatalog.All.GroupBy(s => s.Category))
            {
                Console.WriteLine($"[{g.Key.DisplayName()}]");
                foreach (var s in g)
                {
                    Console.WriteLine($"  {s.Id,-22} {s.Name}");
                }
            }

            return 0;
        }

        var text = ResolveText(rest);

        if (args.Contains("--json"))
        {
            EmitJson(text);
            return 0;
        }

        if (rest.Count >= 2)
        {
            // fancy <样式ID> <文本>
            var style = StyleCatalog.All.FirstOrDefault(s =>
                string.Equals(s.Id, rest[0], StringComparison.OrdinalIgnoreCase));
            if (style is null)
            {
                Console.Error.WriteLine($"未知样式 ID：{rest[0]}（用 --list 查看全部）");
                return 2;
            }

            Console.WriteLine(SafeTransform(style, text));
            return 0;
        }

        if (args.Contains("--random"))
        {
            var applicable = StyleCatalog.All
                .Where(s => !string.Equals(SafeTransform(s, text), text, StringComparison.Ordinal))
                .ToList();
            var pick = applicable.Count == 0 ? StyleCatalog.All[0] : applicable[Random.Shared.Next(applicable.Count)];
            Console.WriteLine(SafeTransform(pick, text));
            return 0;
        }

        // 默认：全部样式表格
        Console.WriteLine($"输入：{text}");
        Console.WriteLine(new string('─', 60));
        foreach (var g in StyleCatalog.All.GroupBy(s => s.Category))
        {
            Console.WriteLine($"[{g.Key.DisplayName()}]");
            foreach (var style in g)
            {
                var output = SafeTransform(style, text);
                if (string.IsNullOrEmpty(output) || string.Equals(output, text, StringComparison.Ordinal))
                {
                    continue; // 不适用（如中文之于纯拉丁映射、摩斯之于纯中文）
                }

                Console.WriteLine($"  {style.Name,-18} → {OneLine(output)}");
            }
        }

        return 0;
    }

    private static string ResolveText(List<string> rest) => rest.Count switch
    {
        0 => StyleCatalog.DefaultSample,
        1 => rest[0],
        _ => string.Join(' ', rest.Skip(1)),
    };

    private static string SafeTransform(TextStyle style, string text)
    {
        try
        {
            return style.Transform(text);
        }
        catch (Exception)
        {
            return text;
        }
    }

    private static void EmitJson(string text)
    {
        var payload = StyleCatalog.All.Select(s => new
        {
            id = s.Id,
            name = s.Name,
            category = s.Category.ToString(),
            output = SafeTransform(s, text),
        });
        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        }));
    }

    private static string OneLine(string text)
    {
        var single = text.ReplaceLineEndings(" ");
        return single.Length <= 80 ? single : single[..80] + "…";
    }
}
