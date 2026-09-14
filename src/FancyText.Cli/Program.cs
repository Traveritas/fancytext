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
///   fancy --import <包.json>  导入样式包（含内置样式 + 已装包的合并目录）
///   fancy --packs             列出已安装的样式包
///   fancy --export <ID...> [--out <包.json>]  把若干样式（如收藏）导出为可分享的包
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // —— 样式包子命令（消费参数并直接返回，不影响旧用法） ——
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--import":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--import 需要包文件路径");
                        return 2;
                    }

                    return ImportPack(args[++i]);

                case "--packs":
                    return ListPacks();

                case "--remove":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--remove 需要包名（用 --packs 查看）");
                        return 2;
                    }

                    return RemovePack(args[++i]);

                case "--export":
                    var ids = new List<string>();
                    string? outFile = null;
                    for (var j = i + 1; j < args.Length; j++)
                    {
                        if (args[j] == "--out")
                        {
                            if (j + 1 >= args.Length)
                            {
                                Console.Error.WriteLine("--out 需要输出文件路径");
                                return 2;
                            }

                            outFile = args[++j];
                        }
                        else if (!args[j].StartsWith("--", StringComparison.Ordinal))
                        {
                            ids.Add(args[j]);
                        }
                    }

                    return ExportStyles(ids, outFile);
            }
        }

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

    // ================================================== 样式包 ==================================================

    private static int ImportPack(string path)
    {
        var result = StylePacks.Import(path);
        if (!result.Success)
        {
            Console.Error.WriteLine("导入失败：");
            foreach (var error in result.Errors)
            {
                Console.Error.WriteLine($"  {error}");
            }

            return 1;
        }

        Console.WriteLine($"已导入 ✓（{result.InstalledPath}）");
        Console.WriteLine("桌面版即时生效；命令面板插件重启后生效。");
        return 0;
    }

    private static int ListPacks()
    {
        var packs = StylePacks.LoadInstalled();
        if (packs.Count == 0)
        {
            Console.WriteLine($"尚未安装样式包（把 .json 放进 {StylePacks.DefaultPacksDirectory} 即可）");
            return 0;
        }

        foreach (var pack in packs)
        {
            if (pack.Error is null)
            {
                Console.WriteLine($"  {pack.PackName,-20} {pack.Styles.Count} 个样式  {pack.FilePath}");
                foreach (var style in pack.Styles)
                {
                    Console.WriteLine($"    {style.Id,-22} {style.Name}");
                }
            }
            else
            {
                Console.WriteLine($"  {Path.GetFileName(pack.FilePath),-20} 损坏：{pack.Error}");
            }
        }

        return 0;
    }

    private static int RemovePack(string packName)
    {
        if (!StylePacks.Remove(packName))
        {
            Console.Error.WriteLine($"没有找到包：{packName}（用 --packs 查看已安装的包名）");
            return 1;
        }

        Console.WriteLine($"已卸载 {packName} ✓");
        return 0;
    }

    private static int ExportStyles(List<string> ids, string? outFile)
    {
        if (ids.Count == 0)
        {
            Console.Error.WriteLine("--export 至少需要一个样式 ID（用 --list 查看全部；收藏样式同样可导出）");
            return 2;
        }

        var byId = StyleCatalog.All.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var styles = new List<TextStyle>();
        var missing = new List<string>();
        foreach (var id in ids)
        {
            if (byId.TryGetValue(id, out var style))
            {
                styles.Add(style);
            }
            else
            {
                missing.Add(id);
            }
        }

        if (missing.Count > 0)
        {
            Console.Error.WriteLine($"未知样式 ID：{string.Join("、", missing)}（用 --list 查看全部）");
            return 2;
        }

        var packName = outFile is null
            ? "导出包"
            : Path.GetFileNameWithoutExtension(outFile);
        var json = StylePacks.Serialize(StylePacks.ExportStyles(styles, packName));
        if (outFile is null)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine(json);
            return 0;
        }

        try
        {
            File.WriteAllText(outFile, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"写入失败：{ex.Message}");
            return 1;
        }

        Console.WriteLine($"已导出 {styles.Count} 个样式 → {outFile}");
        return 0;
    }
}
