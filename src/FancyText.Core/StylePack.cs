using System.Text.Json;

namespace FancyText.Core;

/// <summary>
/// 样式包：可分享的样式集合文件（%LOCALAPPDATA%\FancyText\styles\*.json，一文件一包）。
/// 纯数据（声明式步骤），不含可执行逻辑；格式见 docs 样式包规范。
/// </summary>
public sealed record StylePack
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>包显示名（也决定安装文件名）。</summary>
    public required string Name { get; init; }

    public string? Author { get; init; }

    public string? Description { get; init; }

    public required IReadOnlyList<StyleDefinition> Styles { get; init; }
}

/// <summary>单个样式的校验问题（不阻断整包的其他样式，但会阻断安装）。</summary>
public sealed record PackValidationIssue(string StyleId, string Message);

/// <summary>解析结果：<see cref="Pack"/> 为 null 时 <see cref="Errors"/> 说明原因。</summary>
public sealed record PackParseResult(StylePack? Pack, IReadOnlyList<string> Errors);

/// <summary>导入结果：Success 时 <see cref="InstalledPath"/> 为安装落盘路径。</summary>
public sealed record ImportResult(bool Success, string? InstalledPath, IReadOnlyList<string> Errors);

/// <summary>已安装（或损坏）的包：Error 非空表示该文件解析/编译失败，Styles 为空。</summary>
public sealed record InstalledPack(string PackName, string FilePath, IReadOnlyList<TextStyle> Styles, string? Error);

/// <summary>内嵌官方包：随 FancyText.Core 程序集分发（Resources/bundled/*.json 嵌入资源），由 <see cref="StylePacks.InstallBundled"/> 一键安装。Json 为资源原文。</summary>
public sealed record BundledPack(string PackName, string FileName, IReadOnlyList<TextStyle> Styles, string Json);

/// <summary>
/// 样式包的导入 / 导出 / 安装扫描 / 卸载。三端（插件 / 桌面 / CLI）共用，
/// 目录为 <see cref="DefaultPacksDirectory"/>，一文件一包，放文件即生效（进程下次加载）。
/// </summary>
public static class StylePacks
{
    /// <summary>单文件 2MB 上限：样式包是纯文本数据，正常包远小于此。</summary>
    public const long MaxFileBytes = 2 * 1024 * 1024;

    public static string DefaultPacksDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FancyText", "styles");

    internal static string? DirectoryOverrideForTests;

    private static string ActiveDirectory => DirectoryOverrideForTests ?? DefaultPacksDirectory;

    // ---------- 解析与校验 ----------

    public static PackParseResult ParseFile(string path)
    {
        var fileName = Path.GetFileName(path);
        if (!File.Exists(path))
        {
            return new PackParseResult(null, [$"文件不存在：{fileName}"]);
        }

        if (new FileInfo(path).Length > MaxFileBytes)
        {
            return new PackParseResult(null, [$"文件超过 {MaxFileBytes / 1024} KB 上限：{fileName}"]);
        }

        try
        {
            return ParseJson(File.ReadAllText(path), fileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PackParseResult(null, [$"读取失败：{fileName}（{ex.Message}）"]);
        }
    }

    public static PackParseResult ParseJson(string json, string sourceName)
    {
        StylePack? pack;
        try
        {
            pack = JsonSerializer.Deserialize<StylePack>(json, StylePackJson.PackOptions);
        }
        catch (JsonException ex)
        {
            return new PackParseResult(null, [$"JSON 格式错误：{sourceName}（第 {ex.LineNumber + 1} 行：{ex.Message}）"]);
        }

        if (pack is null)
        {
            return new PackParseResult(null, ["包内容为空"]);
        }

        if (pack.SchemaVersion != StylePack.CurrentSchemaVersion)
        {
            return new PackParseResult(null,
                [$"schemaVersion {pack.SchemaVersion} 不受支持（当前 {StylePack.CurrentSchemaVersion}），请升级 FancyText 或找包作者更新"]);
        }

        if (string.IsNullOrWhiteSpace(pack.Name))
        {
            return new PackParseResult(null, ["包缺少 name"]);
        }

        if (pack.Styles is not { Count: > 0 })
        {
            return new PackParseResult(null, ["包内没有任何样式"]);
        }

        if (pack.Styles.Count > MaxStylesPerPack)
        {
            return new PackParseResult(null, [$"样式数 {pack.Styles.Count} 超过上限 {MaxStylesPerPack}"]);
        }

        var issues = new List<string>();
        foreach (var issue in Validate(pack))
        {
            issues.Add($"[{issue.StyleId}] {issue.Message}");
        }

        return issues.Count > 0
            ? new PackParseResult(null, issues)
            : new PackParseResult(pack, []);
    }

    /// <summary>语义校验：ID 格式与去重、字符串码点健康度（孤立代理/控制字符）、各步骤参数上限、引用白名单。</summary>
    public static IReadOnlyList<PackValidationIssue> Validate(StylePack pack)
    {
        var issues = new List<PackValidationIssue>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var style in pack.Styles)
        {
            var id = style.Id ?? string.Empty;
            void Fail(string message) => issues.Add(new PackValidationIssue(id, message));

            if (string.IsNullOrWhiteSpace(id) || id.Length > MaxIdLength || !IsKebabId(id))
            {
                Fail($"id 必须是 {MaxIdLength} 字符以内的 kebab-case（小写字母/数字/连字符，字母或数字开头）");
            }
            else if (!seenIds.Add(id))
            {
                Fail("包内 id 重复");
            }

            if (string.IsNullOrWhiteSpace(style.Name) || style.Name.Length > MaxNameLength)
            {
                Fail($"name 必须是 {MaxNameLength} 字符以内");
            }

            if (style.NameEn is { Length: > MaxNameLength })
            {
                Fail($"nameEn 超过 {MaxNameLength} 字符");
            }

            if (style.Note?.Length > MaxNoteLength)
            {
                Fail($"note 超过 {MaxNoteLength} 字符");
            }

            if (style.NoteEn is { Length: > MaxNoteLength })
            {
                Fail($"noteEn 超过 {MaxNoteLength} 字符");
            }

            if (style.Steps is not { Count: > 0 })
            {
                Fail("至少需要一个步骤");
                continue;
            }

            if (style.Steps.Count > MaxStepsPerStyle)
            {
                Fail($"步骤数 {style.Steps.Count} 超过上限 {MaxStepsPerStyle}");
            }

            var mapEntries = 0;
            foreach (var step in style.Steps)
            {
                foreach (var problem in ValidateStep(step, allowGuard: true))
                {
                    Fail(problem);
                }

                mapEntries += step switch
                {
                    MapReplaceStep map => map.Map.Count,
                    UseMapStep => 0,
                    _ => 0,
                };
            }

            if (mapEntries > MaxMapEntriesPerStyle)
            {
                Fail($"映射表总条目 {mapEntries} 超过上限 {MaxMapEntriesPerStyle}");
            }
        }

        return issues;
    }

    private static IEnumerable<string> ValidateStep(TransformStep step, bool allowGuard)
    {
        switch (step)
        {
            case MapReplaceStep map:
                foreach (var (key, value) in map.Map)
                {
                    if (!IsHealthyText(value, MaxMappedValueLength))
                    {
                        return
                        [
                            $"映射 '{key}' 的值不合法（{MaxMappedValueLength} 码元以内，禁止孤立代理对与控制字符）",
                        ];
                    }
                }

                break;

            case UseMapStep useMap:
                if (!IsKnownMap(useMap.MapName))
                {
                    return [$"未知内置映射表：{useMap.MapName}"];
                }

                break;

            case AppendMarkStep mark:
                if (!IsHealthyText(mark.Mark, MaxMarkLength))
                {
                    return [$"mark 不合法（{MaxMarkLength} 码元以内，禁止孤立代理对与控制字符）"];
                }

                if (mark.Repeat is < 1 or > MaxRepeat)
                {
                    return [$"repeat 必须在 1–{MaxRepeat} 之间"];
                }

                break;

            case WrapStringStep wrap:
                if (!IsHealthyText(wrap.Prefix, MaxDecoratorLength) || !IsHealthyText(wrap.Suffix, MaxDecoratorLength))
                {
                    return [$"前后缀不合法（各 {MaxDecoratorLength} 码元以内，禁止孤立代理对与控制字符）"];
                }

                break;

            case WrapEachStep wrapEach:
                if (!IsHealthyText(wrapEach.Prefix, MaxDecoratorLength) || !IsHealthyText(wrapEach.Suffix, MaxDecoratorLength))
                {
                    return [$"逐字前后缀不合法（各 {MaxDecoratorLength} 码元以内，禁止孤立代理对与控制字符）"];
                }

                break;

            case SpacingStep spacing:
                if (!IsHealthyText(spacing.Separator, MaxMarkLength))
                {
                    return [$"分隔符不合法（{MaxMarkLength} 码元以内）"];
                }

                break;

            case ReverseStep:
                break;

            case AlgorithmStep algorithm:
                if (algorithm.Algorithm is < 0 or > (KnownAlgorithm)AlgorithmCount)
                {
                    return [$"未知算法：{algorithm.Algorithm}"];
                }

                break;

            case IfChangedStep guard:
                if (!allowGuard || guard.Inner is IfChangedStep)
                {
                    return ["守卫不能嵌套守卫"];
                }

                foreach (var problem in ValidateStep(guard.Inner, allowGuard: false))
                {
                    return [problem];
                }

                break;

            default:
                return [$"不支持的步骤类型：{step.GetType().Name}"];
        }

        return [];
    }

    /// <summary>字符串健康度：长度上限内、无孤立 UTF-16 代理对、无 C0/C1 控制字符（组合附加符号不受影响）。</summary>
    internal static bool IsHealthyText(string text, int maxLength) =>
        text.Length <= maxLength
        && !HasUnpairedSurrogate(text)
        && !ContainsControlCharacter(text);

    private static bool HasUnpairedSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                {
                    return true;
                }

                i++;
            }
            else if (char.IsLowSurrogate(c))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsControlCharacter(string text)
    {
        foreach (var c in text)
        {
            if (char.IsControl(c))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsKebabId(string id)
    {
        if (!char.IsAsciiLetterOrDigit(id[0]))
        {
            return false;
        }

        foreach (var c in id)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsKnownMap(string name)
    {
        try
        {
            _ = KnownTransforms.ResolveMap(name);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // ---------- 安装 / 卸载 / 扫描 ----------

    /// <summary>导入：解析校验 → 冲突检查 → 落盘到包目录（同包名覆盖升级）。</summary>
    public static ImportResult Import(string filePath)
    {
        var parsed = ParseFile(filePath);
        if (parsed.Pack is null)
        {
            return new ImportResult(false, null, parsed.Errors);
        }

        var pack = parsed.Pack;
        var conflicts = FindConflicts(pack);
        if (conflicts.Count > 0)
        {
            return new ImportResult(false, null,
                [$"样式 ID 与内置或其他包冲突（请让包作者改 ID，或先卸载对方包）：{string.Join("、", conflicts)}"]);
        }

        return Install(pack, filePath);
    }

    /// <summary>覆盖安装（同包名 = 升级），冲突只查内置与其他包。</summary>
    private static ImportResult Install(StylePack pack, string? sourcePath)
    {
        try
        {
            Directory.CreateDirectory(ActiveDirectory);
            var target = Path.Combine(ActiveDirectory, ToPackFileName(pack.Name));
            if (sourcePath is not null && !PathsEqual(sourcePath, target))
            {
                File.Copy(sourcePath, target, overwrite: true);

                // 源文件本就在包目录内（下载后直接导入）：移除，避免同一包被扫成两份
                if (PathsEqual(Path.GetDirectoryName(Path.GetFullPath(sourcePath))!, Path.GetFullPath(ActiveDirectory)))
                {
                    File.Delete(sourcePath);
                }
            }
            else
            {
                File.WriteAllText(target, Serialize(pack));
            }

            return new ImportResult(true, target, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ImportResult(false, null, [$"写入失败：{ex.Message}"]);
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>与内置样式及其他已安装包的 ID 冲突（导入阻断用；同包名升级不查自身）。</summary>
    public static IReadOnlyList<string> FindConflicts(StylePack pack)
    {
        var conflicts = new List<string>();
        var incoming = pack.Styles.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var builtIn in StyleCatalog.BuiltInIds)
        {
            if (incoming.Contains(builtIn))
            {
                conflicts.Add($"{builtIn}（内置）");
            }
        }

        foreach (var installed in LoadInstalled())
        {
            if (installed.Error is not null || string.Equals(installed.PackName, pack.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var style in installed.Styles)
            {
                if (incoming.Contains(style.Id))
                {
                    conflicts.Add($"{style.Id}（{installed.PackName}）");
                }
            }
        }

        return conflicts;
    }

    /// <summary>扫描包目录：一文件一包，坏包不拖累其他包（Error 记录原因）。</summary>
    public static IReadOnlyList<InstalledPack> LoadInstalled()
    {
        var result = new List<InstalledPack>();
        if (!Directory.Exists(ActiveDirectory))
        {
            return result;
        }

        foreach (var file in Directory.EnumerateFiles(ActiveDirectory, "*.json"))
        {
            var parsed = ParseFile(file);
            if (parsed.Pack is null)
            {
                result.Add(new InstalledPack(Path.GetFileNameWithoutExtension(file), file, [], string.Join("; ", parsed.Errors)));
                continue;
            }

            var pack = parsed.Pack;
            var styles = new List<TextStyle>();
            foreach (var definition in pack.Styles)
            {
                try
                {
                    styles.Add(StyleFactory.FromDefinition(definition, new StyleSource.Pack(pack.Name)));
                }
                catch (Exception ex)
                {
                    result.Add(new InstalledPack(pack.Name, file, [],
                        $"[{definition.Id}] 编译失败：{ex.Message}"));
                    styles = [];
                    break;
                }
            }

            result.Add(new InstalledPack(pack.Name, file, styles, null));
        }

        return result;
    }

    /// <summary>卸载：按包名删除安装文件。目录里实际文件名是包名转化来的。</summary>
    public static bool Remove(string packName)
    {
        var target = Path.Combine(ActiveDirectory, ToPackFileName(packName));
        if (!File.Exists(target))
        {
            return false;
        }

        File.Delete(target);
        return true;
    }

    /// <summary>按安装记录卸载（直接删该文件；解析失败的坏包同样适用）。</summary>
    public static bool Remove(InstalledPack pack)
    {
        try
        {
            if (!File.Exists(pack.FilePath))
            {
                return false;
            }

            File.Delete(pack.FilePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>合并全部可加载包的样式（跳过坏包与已停用包；与保留 ID（内置/先加载包）冲突的样式跳过）。</summary>
    internal static IReadOnlyList<TextStyle> LoadInstalledStyles(IReadOnlyCollection<string> reservedIds, IReadOnlyCollection<string>? disabledPacks = null)
    {
        var styles = new List<TextStyle>();
        var seen = new HashSet<string>(reservedIds, StringComparer.Ordinal);
        var disabled = disabledPacks is { Count: > 0 } ? new HashSet<string>(disabledPacks, StringComparer.Ordinal) : null;
        foreach (var installed in LoadInstalled())
        {
            if (installed.Error is not null || disabled?.Contains(installed.PackName) == true)
            {
                continue;
            }

            foreach (var style in installed.Styles)
            {
                if (seen.Add(style.Id))
                {
                    styles.Add(style);
                }
            }
        }

        return styles;
    }

    // ---------- 内嵌官方包 ----------

    /// <summary>内嵌官方包的资源名前缀（csproj 把 Resources/bundled/*.json 嵌为程序集资源，见 FancyText.Core.csproj）。</summary>
    public const string BundledResourcePrefix = "FancyText.Core.Resources.bundled.";

    /// <summary>枚举随程序集分发的官方包，经与文件导入相同的 JSON 管线解析并编译（坏资源跳过，不拖累其余包）。</summary>
    public static IReadOnlyList<BundledPack> LoadBundled() => LoadBundledFrom(typeof(StylePacks).Assembly);

    internal static IReadOnlyList<BundledPack> LoadBundledFrom(System.Reflection.Assembly assembly)
    {
        var result = new List<BundledPack>();
        foreach (var resourceName in assembly.GetManifestResourceNames().OrderBy(n => n, StringComparer.Ordinal))
        {
            if (!resourceName.StartsWith(BundledResourcePrefix, StringComparison.Ordinal)
                || !resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string json;
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream is null)
                {
                    continue;
                }

                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                json = reader.ReadToEnd();
            }

            var fileName = resourceName[BundledResourcePrefix.Length..];
            var parsed = ParseJson(json, fileName);
            if (parsed.Pack is not { } pack)
            {
                continue; // 内嵌包随版本发布，解析失败属打包事故：跳过不拖累其余包
            }

            var styles = new List<TextStyle>();
            var broken = false;
            foreach (var definition in pack.Styles)
            {
                try
                {
                    styles.Add(StyleFactory.FromDefinition(definition, new StyleSource.Pack(pack.Name)));
                }
                catch (Exception)
                {
                    broken = true;
                    break;
                }
            }

            if (!broken)
            {
                result.Add(new BundledPack(pack.Name, fileName, styles, json));
            }
        }

        return result;
    }

    /// <summary>一键安装内嵌官方包：解析校验与冲突检查同 <see cref="Import"/>，资源原文字节落盘到包目录（同包名 = 覆盖升级）。</summary>
    public static ImportResult InstallBundled(BundledPack bundled)
    {
        var parsed = ParseJson(bundled.Json, bundled.FileName);
        if (parsed.Pack is null)
        {
            return new ImportResult(false, null, parsed.Errors);
        }

        var pack = parsed.Pack;
        var conflicts = FindConflicts(pack);
        if (conflicts.Count > 0)
        {
            return new ImportResult(false, null,
                [$"样式 ID 与内置或其他包冲突（请先卸载对方包再安装）：{string.Join("、", conflicts)}"]);
        }

        try
        {
            Directory.CreateDirectory(ActiveDirectory);
            var target = Path.Combine(ActiveDirectory, ToPackFileName(pack.Name));
            File.WriteAllText(target, bundled.Json);
            return new ImportResult(true, target, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ImportResult(false, null, [$"写入失败：{ex.Message}"]);
        }
    }

    // ---------- 导出 ----------

    public static StylePack ExportStyles(IEnumerable<TextStyle> styles, string packName, string? author = null, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(packName))
        {
            throw new ArgumentException("包名不能为空", nameof(packName));
        }

        var definitions = new List<StyleDefinition>();
        foreach (var style in styles)
        {
            if (style.Definition is { } definition)
            {
                definitions.Add(definition);
            }
        }

        if (definitions.Count == 0)
        {
            throw new ArgumentException("选中的样式没有可导出的定义", nameof(styles));
        }

        return new StylePack
        {
            Name = packName.Trim(),
            Author = string.IsNullOrWhiteSpace(author) ? null : author!.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description!.Trim(),
            Styles = definitions,
        };
    }

    public static string Serialize(StylePack pack) =>
        JsonSerializer.Serialize(pack, StylePackJson.PackOptions);

    /// <summary>包名 → 安装文件名：保留 Unicode 字母数字（中文包名可直接作文件名），其余字符折叠为连字符。</summary>
    internal static string ToPackFileName(string packName)
    {
        var sb = new System.Text.StringBuilder(packName.Length + 8);
        foreach (var c in packName.Trim())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        while (sb.Length > 0 && sb[^1] == '-')
        {
            sb.Length--;
        }

        if (sb.Length == 0)
        {
            return "pack.json";
        }

        var name = sb.Length > 80 ? sb.ToString(0, 80).TrimEnd('-') : sb.ToString();
        return name + ".json";
    }

    // ---------- 上限常量 ----------

    public const int MaxStylesPerPack = 200;
    public const int MaxStepsPerStyle = 16;
    public const int MaxMapEntriesPerStyle = 2048;
    public const int MaxMappedValueLength = 8;
    public const int MaxMarkLength = 4;
    public const int MaxDecoratorLength = 8;
    public const int MaxRepeat = 8;
    public const int MaxIdLength = 64;
    public const int MaxNameLength = 48;
    public const int MaxNoteLength = 200;

    private const int AlgorithmCount = (int)KnownAlgorithm.StripCombiningMarks;
}
