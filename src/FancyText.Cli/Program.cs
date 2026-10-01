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
///   fancy --packs             列出已安装的样式包（含停用标记）
///   fancy --online            列出在线索引（fancytext-styles 仓库）
///   fancy --online-install <id|包名>  从在线索引下载并安装
///   fancy --remove <包名>     卸载样式包
///   fancy -- <文本>           -- 之后全部当文本（文本以 -- 开头时用）
/// </summary>
public static class Program
{
    private static readonly string[] KnownOptions = ["--list", "--json", "--random", "--demo", "--help", "--version"];

    private const string Usage = """
        fancy [文本]                     全部样式预览
        fancy <样式ID> <文本>            单样式转换，只输出结果
        fancy --list                     样式 ID 清单
        fancy --random [文本]            随机样式
        fancy --json [文本]              JSON 输出全部结果
        fancy --import <包.json>         导入样式包
        fancy --packs                    列出已安装样式包
        fancy --remove <包名>            卸载样式包
        fancy --online                   列出在线索引
        fancy --online-install <id>      从在线索引安装
        fancy --version                  版本号
        fancy -- <文本>                  -- 之后全部当文本
        """;

    /// <summary>界面语言：跟随共享 state.json 的语言偏好（桌面版/插件切换后 CLI 同步），无则系统文化。</summary>
    private static AppLanguage Lang { get; } = Localization.Resolve(new UsageState().Language);

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
                        Console.Error.WriteLine(Loc.S(Lang, "--import 需要包文件路径", "--import requires a pack file path"));
                        return 2;
                    }

                    return ImportPack(args[++i]);

                case "--packs":
                    return ListPacks();

                case "--online":
                    return ListOnlinePacks().GetAwaiter().GetResult();

                case "--online-install":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine(Loc.S(Lang, "--online-install 需要索引 id 或包名（用 --online 查看）", "--online-install requires an index id or pack name (see --online)"));
                        return 2;
                    }

                    return InstallOnlinePack(args[++i]).GetAwaiter().GetResult();

                case "--remove":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine(Loc.S(Lang, "--remove 需要包名（用 --packs 查看）", "--remove requires a pack name (see --packs)"));
                        return 2;
                    }

                    return RemovePack(args[++i]);

                case "--":
                    i = args.Length; // 其后全是文本，不再找子命令
                    break;
            }
        }

        // 拆分选项与位置参数；"--" 之后的一切都当文本（允许文本本身以 -- 开头）
        var options = new HashSet<string>(StringComparer.Ordinal);
        var rest = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--")
            {
                rest.AddRange(args.Skip(i + 1));
                break;
            }

            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                options.Add(args[i]);
            }
            else
            {
                rest.Add(args[i]);
            }
        }

        var unknown = options.Except(KnownOptions).ToList();
        if (unknown.Count > 0)
        {
            Console.Error.WriteLine(Loc.S(Lang, $"未知选项：{string.Join(" ", unknown)}（用 --help 查看用法）", $"Unknown option: {string.Join(" ", unknown)} (see --help)"));
            return 2;
        }

        if (options.Contains("--help"))
        {
            Console.WriteLine(Usage);
            return 0;
        }

        if (options.Contains("--version"))
        {
            Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "?");
            return 0;
        }

        if (options.Contains("--list"))
        {
            foreach (var g in StyleCatalog.All.GroupBy(s => s.CategoryKey))
            {
                Console.WriteLine($"[{g.First().GetCategoryName(Lang)}]");
                foreach (var s in g)
                {
                    Console.WriteLine($"  {s.Id,-22} {s.GetName(Lang)}");
                }
            }

            return 0;
        }

        // --json / --random 不接受样式 ID，位置参数全部是文本；否则两个以上位置参数时首个是样式 ID
        var wholeText = options.Contains("--json") || options.Contains("--random");
        var text = rest.Count == 0
            ? StyleCatalog.DefaultSample
            : string.Join(' ', wholeText || rest.Count == 1 ? rest : rest.Skip(1));

        if (options.Contains("--json"))
        {
            EmitJson(text);
            return 0;
        }

        if (!wholeText && rest.Count >= 2)
        {
            // fancy <样式ID> <文本>
            var style = StyleCatalog.All.FirstOrDefault(s =>
                string.Equals(s.Id, rest[0], StringComparison.OrdinalIgnoreCase));
            if (style is null)
            {
                Console.Error.WriteLine(Loc.S(Lang, $"未知样式 ID：{rest[0]}（用 --list 查看全部）", $"Unknown style ID: {rest[0]} (see --list)"));
                return 2;
            }

            Console.WriteLine(SafeTransform(style, text));
            return 0;
        }

        if (options.Contains("--random"))
        {
            var applicable = StyleCatalog.All
                .Where(s => !string.Equals(SafeTransform(s, text), text, StringComparison.Ordinal))
                .ToList();
            var pick = applicable.Count == 0 ? StyleCatalog.All[0] : applicable[Random.Shared.Next(applicable.Count)];
            Console.WriteLine(SafeTransform(pick, text));
            return 0;
        }

        // 默认：全部样式表格
        Console.WriteLine(Loc.S(Lang, $"输入：{text}", $"Input: {text}"));
        Console.WriteLine(new string('─', 60));
        foreach (var g in StyleCatalog.All.GroupBy(s => s.CategoryKey))
        {
            Console.WriteLine($"[{g.First().GetCategoryName(Lang)}]");
            foreach (var style in g)
            {
                var output = SafeTransform(style, text);
                if (string.IsNullOrEmpty(output) || string.Equals(output, text, StringComparison.Ordinal))
                {
                    continue; // 不适用（如中文之于纯拉丁映射、摩斯之于纯中文）
                }

                Console.WriteLine($"  {style.GetName(Lang),-18} → {OneLine(output)}");
            }
        }

        return 0;
    }

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
            category = s.CategoryKey,
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
            Console.Error.WriteLine(Loc.S(Lang, "导入失败：", "Import failed:"));
            foreach (var error in result.Errors)
            {
                Console.Error.WriteLine($"  {error}");
            }

            return 1;
        }

        Console.WriteLine(Loc.S(Lang, $"已导入 ✓（{result.InstalledPath}）", $"Imported ✓ ({result.InstalledPath})"));
        WriteWarnings(result);
        return 0;
    }

    private static void WriteWarnings(ImportResult result)
    {
        foreach (var warning in result.Warnings)
        {
            Console.Error.WriteLine(Loc.S(Lang, $"  注意：{warning}", $"  Note: {warning}"));
        }
    }

    private static int ListPacks()
    {
        var packs = StylePacks.LoadInstalled();
        if (packs.Count == 0)
        {
            Console.WriteLine(Loc.S(Lang,
                $"尚未安装样式包（把 .json 放进 {StylePacks.DefaultPacksDirectory} 即可）",
                $"No style packs installed (drop a .json into {StylePacks.DefaultPacksDirectory})"));
            return 0;
        }

        var state = new UsageState();
        foreach (var pack in packs)
        {
            if (pack.Error is null)
            {
                var disabled = state.IsPackDisabled(pack.PackName);
                Console.WriteLine(Loc.S(Lang,
                    $"  {pack.PackName,-20} {pack.Styles.Count} 个样式{(disabled ? "（已停用）" : "")}  {pack.FilePath}",
                    $"  {pack.PackName,-20} {pack.Styles.Count} styles{(disabled ? " (disabled)" : "")}  {pack.FilePath}"));
                foreach (var style in pack.Styles)
                {
                    Console.WriteLine($"    {style.Id,-22} {style.GetName(Lang)}");
                }
            }
            else
            {
                Console.WriteLine(Loc.S(Lang,
                    $"  {Path.GetFileName(pack.FilePath),-20} 损坏：{pack.Error}",
                    $"  {Path.GetFileName(pack.FilePath),-20} broken: {pack.Error}"));
            }
        }

        return 0;
    }

    private static int RemovePack(string packName)
    {
        if (!StylePacks.Remove(packName))
        {
            Console.Error.WriteLine(Loc.S(Lang,
                $"没有找到包：{packName}（用 --packs 查看已安装的包名）",
                $"Pack not found: {packName} (see --packs for installed pack names)"));
            return 1;
        }

        Console.WriteLine(Loc.S(Lang, $"已卸载 {packName} ✓", $"Removed {packName} ✓"));
        return 0;
    }

    private static async Task<int> ListOnlinePacks()
    {
        var result = await StyleRegistry.FetchIndexAsync();
        if (result.Value is null)
        {
            Console.Error.WriteLine(result.Error);
            return 1;
        }

        var installed = StylePacks.LoadInstalled().Where(p => p.Error is null)
            .Select(p => p.PackName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in result.Value)
        {
            var mark = installed.Contains(entry.PackName) ? " ✓" : "";
            Console.WriteLine($"{entry.Id,-16} {entry.PackName}{mark}{(entry.Official ? Loc.S(Lang, "  [官方]", "  [official]") : "")}");
            if (!string.IsNullOrEmpty(entry.Description))
            {
                Console.WriteLine($"{"",-16} {entry.Description}");
            }
        }

        return 0;
    }

    private static async Task<int> InstallOnlinePack(string idOrName)
    {
        var index = await StyleRegistry.FetchIndexAsync();
        if (index.Value is null)
        {
            Console.Error.WriteLine(index.Error);
            return 1;
        }

        var entry = index.Value.FirstOrDefault(e =>
            string.Equals(e.Id, idOrName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.PackName, idOrName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            Console.Error.WriteLine(Loc.S(Lang, $"索引里没有：{idOrName}（用 --online 查看）", $"Not in index: {idOrName} (see --online)"));
            return 1;
        }

        var download = await StyleRegistry.DownloadPackAsync(entry);
        if (download.Value is null)
        {
            Console.Error.WriteLine(download.Error);
            return 1;
        }

        var import = StylePacks.ImportJson(download.Value, $"在线:{entry.Id}");
        if (!import.Success)
        {
            Console.Error.WriteLine(string.Join("; ", import.Errors));
            return 1;
        }

        Console.WriteLine(Loc.S(Lang, $"已安装「{entry.PackName}」→ {import.InstalledPath}", $"Installed \"{entry.PackName}\" -> {import.InstalledPath}"));
        WriteWarnings(import);
        return 0;
    }
}
