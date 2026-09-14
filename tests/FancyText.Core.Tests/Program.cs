using System.Diagnostics;
using System.Text;
using FancyText.Core;

namespace FancyText.Core.Tests;

/// <summary>轻量自检测试台：dotnet run 即执行，失败时退出码非 0。</summary>
public static class Program
{
    private static int _passed;
    private static int _failed;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Contains("demo"))
        {
            RunDemo();
            return 0;
        }

        if (args.Contains("bench"))
        {
            RunBench();
            return 0;
        }

        TestPrimitives();
        TestLatinMaps();
        TestCombiningStyles();
        TestZalgo();
        TestMartian();
        TestEncodings();
        TestCatalogIntegrity();
        TestExpandedStyles();
        TestReviewFixes();
        TestDeclarativeEquivalence();
        TestStylePacks();

        Console.WriteLine();
        Console.WriteLine($"通过 {_passed} 项，失败 {_failed} 项");
        return _failed == 0 ? 0 : 1;
    }

    /// <summary>体验评审（v1.0.0）修复项的回归测试。</summary>
    private static void TestReviewFixes()
    {
        Console.WriteLine("评审修复回归：");
        // italic 小写 h 为未分配码位，必须落在洞字符 ℎ U+210E
        CheckEqual(U(0x1D456, 0x210E), TextTransforms.MapReplace("ih", LatinMaps.Italic), "Italic h→ℎ（未分配码位修复）");
        // 纯中文对倒转/镜像映射零命中 → 返回原文（由 UI 过滤），不再伪装成"倒转"
        Check(TextTransforms.MapAndReverse("你好", LatinMaps.UpsideDown) == "你好", "倒转映射零命中返回原文");
        Check(TextTransforms.MapAndReverse("你好", LatinMaps.Mirror) == "你好", "镜像映射零命中返回原文");
        Check(!TextTransforms.MapAndReverse("你好abc", LatinMaps.UpsideDown).Equals("你好abc", StringComparison.Ordinal), "混合输入倒转仍生效");
        // 宽体对纯 CJK 退化为分字空格 → 视为不适用（返回原文）
        Check(Find("spacing-wide").Transform("你好") == "你好", "宽体对纯中文不适用");
        Check(Find("spacing-wide").Transform("Hi") != "Hi", "宽体对拉丁生效");
        // 摩斯对纯中文输出为空（UI 过滤的前提）
        Check(LatinMaps.ToMorse("你好") == string.Empty, "摩斯对纯中文输出空串");
        // 新增大小写变换
        Check(Find("uppercase").Transform("aBc你好") == "ABC你好", "全大写");
        Check(Find("lowercase").Transform("aBc你好") == "abc你好", "全小写");
        Check(Find("alternating-case").Transform("abc def") == "aBc DeF", "交替大小写");
        // 已删除名不符实的笑脸文
        Check(StyleCatalog.All.All(s => s.Id != "smiley"), "笑脸文已删除");
    }

    /// <summary>按码点拼字符串，避免源码里形近字（ℯ/е、ℨ/ℤ、Ⱨ/ⱨ）写错。</summary>
    private static string U(params int[] codePoints) =>
        string.Concat(codePoints.Select(char.ConvertFromUtf32));

    private static void Check(bool condition, string name, string? detail = null)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  ✓ {name}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"  ✗ {name}{(detail == null ? "" : $" —— {detail}")}");
        }
    }

    private static void CheckEqual(string expected, string actual, string name) =>
        Check(expected == actual, name, $"期望 \"{expected}\"，实际 \"{actual}\"");

    private static void RunDemo()
    {
        var text = args0();
        Console.WriteLine($"输入：{text}\n");
        foreach (var style in StyleCatalog.All)
        {
            string output;
            try
            {
                output = style.Transform(text);
            }
            catch (Exception ex)
            {
                output = $"（出错：{ex.Message}）";
            }

            Console.WriteLine($"[{style.CategoryDisplayName}] {style.Name,-16} → {output}");
        }

        return;

        static string args0() =>
            Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(a => a != "demo") is { Length: > 0 } custom
                ? custom
                : StyleCatalog.DefaultSample;
    }

    /// <summary>引擎分配/耗时基准：N 次全目录转换 64 字输入，观察托管内存与 GC 次数。</summary>
    private static void RunBench()
    {
        const int iterations = 200;
        var previewInput = new string('测', 16) + new string('a', 16) + "0123456789012345678901234567890"; // 64 个字素

        foreach (var style in StyleCatalog.All)
        {
            _ = style.Transform(previewInput); // 预热（含静态字典初始化）
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var managedBefore = GC.GetTotalMemory(true);

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            foreach (var style in StyleCatalog.All)
            {
                _ = style.Transform(previewInput);
            }
        }

        sw.Stop();
        Console.WriteLine($"{iterations} 次全目录转换（{StyleCatalog.All.Count} 样式 × 64 字输入）：");
        Console.WriteLine($"  平均耗时 {sw.Elapsed.TotalMilliseconds / iterations:F2} ms/次");
        Console.WriteLine($"  托管内存净增 {(GC.GetTotalMemory(false) - managedBefore) / 1024.0:F0} KB");
        Console.WriteLine($"  GC 次数 gen0={GC.CollectionCount(0)} gen1={GC.CollectionCount(1)} gen2={GC.CollectionCount(2)}");
    }

    private static void TestPrimitives()
    {
        Console.WriteLine("原语：");
        CheckEqual("𝐇𝐞𝐥𝐥𝐨", TextTransforms.MapReplace("Hello", LatinMaps.Bold), "MapReplace 粗体 Hello");
        CheckEqual("好\u0488", TextTransforms.AppendMark("好", "\u0488"), "AppendMark 菊花单字");
        CheckEqual("a\u0336b\u0336", TextTransforms.AppendMark("ab", "\u0336"), "AppendMark 双字");
        CheckEqual("꧁你好꧂", TextTransforms.WrapString("你好", "꧁", "꧂"), "WrapString 翅膀");
        CheckEqual("(a)(b)", TextTransforms.WrapEach("ab", "(", ")"), "WrapEach 逐字包围");
        CheckEqual("H e l l o", TextTransforms.Spacing("Hello", " "), "Spacing 分字");
        CheckEqual("cba", TextTransforms.Reverse("abc"), "Reverse 倒序");
        CheckEqual("你 好", TextTransforms.Spacing("你好", " "), "Spacing 中文");
        // 增补平面字符不被拆散
        CheckEqual(U(0x62, 0x1D41A), TextTransforms.Reverse(U(0x1D41A, 0x62)), "Reverse 增补平面按字素处理");
        CheckEqual("你好", TextTransforms.StripCombiningMarks("你\u0488好\u0336\u0305"), "StripCombiningMarks 去装饰");
        Check(TextTransforms.MapReplace("", LatinMaps.Bold) == "", "空输入安全");
        Check(TextTransforms.AppendMark("", "\u0488") == "", "空输入安全（组合符）");
    }

    private static void TestLatinMaps()
    {
        Console.WriteLine("花体映射表：");
        CheckEqual("𝐇𝐞𝐥𝐥𝐨", TextTransforms.MapReplace("Hello", LatinMaps.Bold), "Bold");
        CheckEqual("𝟏𝟐𝟑", TextTransforms.MapReplace("123", LatinMaps.Bold), "Bold 数字");
        CheckEqual("𝐻𝑒𝑙𝑙𝑜", TextTransforms.MapReplace("Hello", LatinMaps.Italic), "Italic");
        CheckEqual("𝓗𝓮𝓵𝓵𝓸", TextTransforms.MapReplace("Hello", LatinMaps.BoldScript), "BoldScript 花体");
        CheckEqual(U(0x210B, 0x212F, 0x1D4C1, 0x1D4C1, 0x2134),
            TextTransforms.MapReplace("Hello", LatinMaps.Script), "Script（H/e/o 为洞字符）");
        CheckEqual(U(0x212C), TextTransforms.MapReplace("B", LatinMaps.Script), "Script 洞字符 B→ℬ");
        CheckEqual(U(0x212D), TextTransforms.MapReplace("C", LatinMaps.Fraktur), "Fraktur 洞字符 C→ℭ");
        CheckEqual(U(0x2128), TextTransforms.MapReplace("Z", LatinMaps.Fraktur), "Fraktur 洞字符 Z→ℨ");
        CheckEqual(U(0x2102), TextTransforms.MapReplace("C", LatinMaps.DoubleStruck), "DoubleStruck 洞字符 C→ℂ");
        CheckEqual("𝟙𝟚𝟛", TextTransforms.MapReplace("123", LatinMaps.DoubleStruck), "DoubleStruck 数字");
        CheckEqual("𝙷𝚎𝚕𝚕𝚘", TextTransforms.MapReplace("Hello", LatinMaps.Monospace), "Monospace");
        CheckEqual("Ⓗⓔⓛⓛⓞ", TextTransforms.MapReplace("Hello", LatinMaps.Circled), "Circled 大写");
        CheckEqual("ⓐⓑⓩ", TextTransforms.MapReplace("abz", LatinMaps.Circled), "Circled 小写");
        CheckEqual("①②③", TextTransforms.MapReplace("123", LatinMaps.Circled), "Circled 数字");
        CheckEqual("🅗🅔🅛🅛🅞", TextTransforms.MapReplace("Hello", LatinMaps.CircledNegative), "CircledNegative");
        CheckEqual("🄷🄴🄻🄻🄾", TextTransforms.MapReplace("Hello", LatinMaps.Squared), "Squared");
        CheckEqual("🅷🅴🅻🅻🅾", TextTransforms.MapReplace("Hello", LatinMaps.SquaredNegative), "SquaredNegative");
        CheckEqual("🄗⒠⒧⒧⒪", TextTransforms.MapReplace("Hello", LatinMaps.Parenthesized), "Parenthesized");
        CheckEqual("Ｈｅｌｌｏ", TextTransforms.MapReplace("Hello", LatinMaps.Fullwidth), "Fullwidth");
        CheckEqual("Ａ１！", TextTransforms.MapReplace("A1!", LatinMaps.Fullwidth), "Fullwidth 数字标点");
        CheckEqual("　", TextTransforms.MapReplace(" ", LatinMaps.Fullwidth), "Fullwidth 空格→全角空格");
        CheckEqual("ʜᴇʟʟᴏ", TextTransforms.MapReplace("hello", LatinMaps.SmallCaps), "SmallCaps");
        CheckEqual("ʷᵒʷ", TextTransforms.MapReplace("wow", LatinMaps.Superscript), "Superscript");
        CheckEqual("⁵²⁰", TextTransforms.MapReplace("520", LatinMaps.Superscript), "Superscript 数字");
        CheckEqual("ₕₑₗₗₒ", TextTransforms.MapReplace("hello", LatinMaps.Subscript), "Subscript");
        CheckEqual("ₛᵤₚ", TextTransforms.MapReplace("sup", LatinMaps.Subscript), "Subscript s u p");
        CheckEqual(U(0x20B5, 0x20B3, 0x20B4, 0x2C67),
            TextTransforms.MapReplace("cash", LatinMaps.Currency), "Currency");
        CheckEqual("ค๒ς", TextTransforms.MapReplace("abc", LatinMaps.SymbolsMix), "SymbolsMix");
        CheckEqual("31173", TextTransforms.MapReplace("elite", LatinMaps.Leet), "Leet");
        CheckEqual("ollǝɥ", TextTransforms.MapAndReverse("hello", LatinMaps.UpsideDown), "UpsideDown hello");
        CheckEqual("6ㄥ", TextTransforms.MapAndReverse("79", LatinMaps.UpsideDown), "UpsideDown 数字 7 9");
        Check(TextTransforms.MapReplace("W", LatinMaps.UpsideDown) == "M", "UpsideDown W→M");
        Check(TextTransforms.MapAndReverse("abc", LatinMaps.Mirror) != "abc", "Mirror 生效");
        Check(LatinMaps.Mirror['E'] == U(0x018E) && LatinMaps.Mirror['R'] == "Я", "Mirror E→Ǝ R→Я");
    }

    private static void TestCombiningStyles()
    {
        Console.WriteLine("组合符样式（经 StyleCatalog）：");
        var juhua = Find("juhua-1");
        CheckEqual("你҈好҈", juhua.Transform("你好"), "菊花体 ❶");
        var juhua2 = Find("juhua-2");
        CheckEqual("你҉好҉", juhua2.Transform("你好"), "菊花体 ❷");
        CheckEqual("H̶e̶l̶l̶o̶", Find("strikethrough").Transform("Hello"), "删除线");
        Check(Find("enclose-circle").Transform("好") == "好⃝", "圈圈字 U+20DD");
        Check(Find("bird-1").Transform("好") == "好\u0F7C", "飞鸟文 藏文 U+0F7C");
        Check(Find("vine-1").Transform("好") == "ζั͡好✿", "花藤字 ❶ 藤头+尾花");
        Check(Find("wing-classic").Transform("你好") == "꧁你好꧂", "经典翅膀");
        Check(Find("wing-fancy").Transform("你好") == "꧁༺你好༻꧂", "华丽翅膀");
        Check(Find("border-star").Transform("hi") == "★彡hi彡★", "星光边框");
        Check(Find("spacing-aesthetic").Transform("你好") == "你 好", "分字空格（中文）");
    }

    private static void TestZalgo()
    {
        Console.WriteLine("Zalgo：");
        var seeded = new Random(42);
        var result = ZalgoTransformer.Transform("你好", ZalgoIntensity.Normal, seeded);
        Check(result.Length > "你好".Length, "Zalgo 输出加长");
        Check(ZalgoTransformer.Strip(result) == "你好", "Zalgo 可完整还原");
        var max = ZalgoTransformer.Transform("ab", ZalgoIntensity.Max, new Random(7));
        Check(max.Length > "ab".Length * 4, "Max 强度叠加更多组合符");
        Check(ZalgoTransformer.Strip(Find("strikethrough").Transform("abc")) == "abc", "删除线样式也可还原");
    }

    private static void TestMartian()
    {
        Console.WriteLine("火星文：");
        Check(MartianDictionary.EntryCount >= 2000, $"字典条目数 {MartianDictionary.EntryCount} ≥ 2000");
        Check(MartianDictionary.Map.ContainsKey('我'), "字典覆盖常用字「我」");
        Check(MartianDictionary.Map.ContainsKey('的'), "字典覆盖常用字「的」");
        var martian = Find("martian").Transform("我爱你");
        Check(martian != "我爱你" && martian.Length == 3, $"火星文「我爱你」→「{martian}」");
    }

    private static void TestEncodings()
    {
        Console.WriteLine("编码：");
        var b64 = Find("base64").Transform("Hello 你好");
        Check(Convert.FromBase64String(b64).SequenceEqual(Encoding.UTF8.GetBytes("Hello 你好")), "Base64 往返一致");
        CheckEqual("Uryyb", LatinMaps.Rot13("Hello"), "ROT13");
        CheckEqual("Hello", LatinMaps.Rot13(LatinMaps.Rot13("Hello")), "ROT13 两次还原");
        CheckEqual("... --- ...", Find("morse").Transform("SOS"), "摩斯电码 SOS");
        CheckEqual(".- / -...", Find("morse").Transform("a b"), "摩斯电码 词间 /");
    }

    private static void TestCatalogIntegrity()
    {
        Console.WriteLine("目录完整性：");
        Check(StyleCatalog.All.Count >= 70, $"样式总数 {StyleCatalog.All.Count} ≥ 70");
        var ids = StyleCatalog.All.Select(s => s.Id).ToList();
        Check(ids.Count == ids.Distinct().Count(), "样式 ID 无重复");
        Check(StyleCatalog.All.All(s => !string.IsNullOrWhiteSpace(s.Name)), "所有样式都有名称");
        Check(StyleCatalog.All.GroupBy(s => s.Category).Count() >= 5, "分类数量 ≥ 5");

        var samples = new[] { "Hello 你好 123", "The quick brown fox jumps over the lazy dog", "你好，世界！", "" };
        var exceptions = new List<string>();
        foreach (var style in StyleCatalog.All)
        {
            foreach (var sample in samples)
            {
                try
                {
                    var output = style.Transform(sample);
                    if (output == null)
                    {
                        exceptions.Add($"{style.Id}: 返回 null");
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add($"{style.Id}: {ex.Message}");
                }
            }
        }

        Check(exceptions.Count == 0, "全部样式对中英文样本无异常", string.Join("; ", exceptions.Take(5)));

        // 中文 + 纯拉丁样式：不应改变中文部分
        var bold = Find("bold").Transform("汉字abc");
        Check(bold.Contains("汉字"), "拉丁样式保留中文字符");

        // 还原样式对全目录的中英样本一致性
        foreach (var id in new[] { "juhua-1", "strikethrough", "smoke", "sprout", "zalgo-normal" })
        {
            var transformed = Find(id).Transform("测试Ab1");
            Check(TextTransforms.StripCombiningMarks(transformed) == "测试Ab1", $"还原样式可清除 {id}");
        }
    }

    private static void TestExpandedStyles()
    {
        Console.WriteLine("扩充样式：");
        // 盲文（grade-1，逐字母点位表）
        CheckEqual("⠓⠑⠇⠇⠕", LatinMaps.ToBraille("hello"), "盲文 hello");
        CheckEqual("⠼⠁⠃⠉", LatinMaps.ToBraille("123"), "盲文数字符 ⠼ + a–j 形");
        CheckEqual("⠓⠑⠇⠇⠕", Find("braille").Transform("HeLLo"), "盲文大小写同形");
        // 翅膀样例（码点精确断言）
        CheckEqual(U(0xA9C1, 0x2766, 0x0F3A) + "你好" + U(0x0F3B, 0x2766, 0xA9C2), Find("wing-elegant").Transform("你好"), "典雅翅膀");
        CheckEqual(U(0xA9C1, 0x0F3A, 0x25C7) + "你好" + U(0x25C7, 0x0F3B, 0xA9C2), Find("wing-diamond").Transform("你好"), "钻石翅膀");
        CheckEqual(U(0xA9C1, 0x0F3A, 0x0F12) + "你好" + U(0x0F12, 0x0F3B, 0xA9C2), Find("wing-double").Transform("你好"), "双层翅膀");
        CheckEqual(U(0x0F3A, 0x0FC8) + "你好" + U(0x0FC8, 0x0F3B), Find("wing-sanskrit").Transform("你好"), "藏文花结翅膀");
        CheckEqual(U(0x0F04, 0x0F0A) + "你好" + U(0x0F7C, 0x0F82, 0x0F7E, 0x0FC6, 0x0FD0), Find("wing-charm").Transform("你好"), "护符边框");
        CheckEqual(U(0xA9C1, 0x0F3A, 0x0F3D, 0x0F3E, 0x0F4A) + "你好" + U(0x0F4F, 0x0F3F, 0x0F3C, 0x0F3B, 0xA9C2), Find("wing-retro").Transform("你好"), "复古花纹");
        CheckEqual(U(0x263E, 0x0F3A) + "你好" + U(0x0F3B, 0x263D), Find("wing-moonlight").Transform("你好"), "月华装饰");
        CheckEqual(U(0x0DC6) + "你好" + U(0x0DC6), Find("wing-heart").Transform("你好"), "甜心括号");
        CheckEqual(U(0x208D, 0x1422) + "你好" + U(0x1422, 0x208E), Find("wing-bunny").Transform("你好"), "兔系装饰");
        // 组合符预设
        Check(Find("paren-marks").Transform("好") == "好" + U(0x035C, 0x0361), "连弧文 U+035C+U+0361");
        Check(Find("stripes").Transform("好") == "好" + U(0x035E, 0x035F), "横条纹 U+035E+U+035F");
        Check(Find("lightning").Transform("好") == "好" + U(0x035B), "闪电文 U+035B");
        Check(Find("manipuri-underline").Transform("好") == "好" + U(0xAADF), "曼尼普尔下划线 U+AADF");
        Check(Find("lace").Transform("好") == "好" + U(0x0B8A), "花边文 U+0B8A");
        Check(Find("grass").Transform("好") == "好" + U(0x0DF4), "草头文 U+0DF4");
        // 数字变体
        CheckEqual("➀➁➂", Find("digits-circled-sans").Transform("123"), "无衬线圈数字 123");
        CheckEqual("⓵⓶", Find("digits-double-circled").Transform("12"), "双圈数字 12");
        Check(Find("digits-circled-sans").Transform("0") == "0", "无衬线圈数字 0 回退原字");
        Check(Find("digits-double-circled").Transform("abc") == "abc", "双圈数字字母回退原字");
        // A1Z26 / NATO / 十六进制 / 二进制
        CheckEqual("1-2", Find("a1z26").Transform("ab"), "A1Z26 ab");
        CheckEqual("1-2 3-4", Find("a1z26").Transform("ab cd"), "A1Z26 词间空格、字母间连字符");
        CheckEqual("Alfa Bravo", Find("nato").Transform("ab"), "NATO ab");
        CheckEqual("Hotel Echo Lima Lima Oscar", Find("nato").Transform("Hello"), "NATO Hello");
        CheckEqual("41 42", Find("hex").Transform("AB"), "十六进制 AB");
        CheckEqual("01000001 01000010", Find("binary").Transform("AB"), "二进制 AB");
        // 火星文还原（往返：简体 → 火星文 → 简体）
        var wo = Find("martian").Transform("我");
        Check(wo != "我" && Find("martian-reverse").Transform(wo) == "我", "火星文往返「我」");
        var ai = Find("martian").Transform("爱");
        Check(ai != "爱" && Find("martian-reverse").Transform(ai) == "爱", "火星文往返「爱」");
        var ni = Find("martian").Transform("你");
        Check(ni != "你" && Find("martian-reverse").Transform(ni) == "你", "火星文往返「你」");
        // 俄化
        CheckEqual(U(0x0412) + "oss", Find("cyrillic-lookalike").Transform("Boss"), "俄化 Boss 首字母变西里尔");
        CheckEqual(U(0x0420, 0x0415, 0x0422), Find("cyrillic-lookalike").Transform("PET"), "俄化 PET→РЕТ");
        CheckEqual("R" + U(0x0415) + "S" + U(0x0422), Find("cyrillic-lookalike").Transform("REST"), "俄化 REST（R/S 无形近字回退）");
        // 目录完整性（扩充后）
        Check(StyleCatalog.All.Count >= 105, $"样式总数 {StyleCatalog.All.Count} ≥ 105");
        var ids = StyleCatalog.All.Select(s => s.Id).ToList();
        Check(ids.Count == ids.Distinct().Count(), "扩充后样式 ID 无重复");
        var exceptions = new List<string>();
        foreach (var style in StyleCatalog.All)
        {
            foreach (var sample in new[] { "Hello 你好 123", "" })
            {
                try
                {
                    if (style.Transform(sample) == null)
                    {
                        exceptions.Add($"{style.Id}: 返回 null");
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add($"{style.Id}: {ex.Message}");
                }
            }
        }

        Check(exceptions.Count == 0, "全部样式对样本与空串无异常", string.Join("; ", exceptions.Take(5)));
    }

    /// <summary>
    /// 声明式目录自检：每个样式的 Definition 重新编译后与目录中 Transform 输出一致（防解释器回归），
    /// 外加管道组合与守卫（IfChanged 零命中短路）语义的定向用例。
    /// </summary>
    private static void TestDeclarativeEquivalence()
    {
        Console.WriteLine("声明式目录：");
        Check(StyleCatalog.All.All(s => s.Definition != null), "全部样式携带声明式定义");
        Check(StyleCatalog.All.Count(s => s.Source is null or StyleSource.BuiltIn) == StyleCatalog.BuiltInIds.Count,
            "内置样式数量与来源标记正确（不受已安装包影响）");
        Check(StyleCatalog.All.All(s => s.Definition!.Steps.Count > 0), "全部定义至少一个步骤");

        var samples = new[] { "Hello 你好 123", "abz ABZ 019", "你好，世界！", "" };
        var mismatches = new List<string>();
        foreach (var style in StyleCatalog.All)
        {
            var recompiled = StyleInterpreter.Compile(style.Definition!.Steps);
            foreach (var sample in samples)
            {
                if (style.Id is "zalgo-mini" or "zalgo-normal" or "zalgo-max")
                {
                    continue; // 随机算法无法逐字节对比
                }

                if (recompiled(sample) != style.Transform(sample))
                {
                    mismatches.Add(style.Id);
                    break;
                }
            }
        }

        Check(mismatches.Count == 0, "定义重编译输出与目录一致", string.Join("; ", mismatches.Take(5)));

        // 管道组合与守卫语义的定向用例
        var pipeOnly = StyleInterpreter.Compile(
        [
            new WrapEachStep("ζั͡", ""), new WrapStringStep("", "✿"),
        ]);
        Check(pipeOnly("好") == Find("vine-1").Transform("好"), "管道组合（WrapEach→WrapString）与 vine-1 一致");
        var guarded = StyleInterpreter.Compile(
        [
            new UseMapStep("fullwidth"), new IfChangedStep(new SpacingStep(" ")),
        ]);
        Check(guarded("你好") == "你好" && guarded("Hi") == Find("spacing-wide").Transform("Hi"), "守卫零命中短路（宽体）");
        Check(StyleInterpreter.Compile([new UseMapStep("upside-down"), new IfChangedStep(new ReverseStep())])("hello")
            == Find("upside-down").Transform("hello"), "守卫应用（倒转=映射+倒序）");
    }

    /// <summary>样式包：JSON 解析（全部步骤类型）、导出往返、坏输入校验、安装/扫描/卸载/冲突。</summary>
    private static void TestStylePacks()
    {
        Console.WriteLine("样式包：");
        const string packJson = """
        {
          "schemaVersion": 1,
          "name": "测试包",
          "author": "tester",
          "description": "覆盖全部步骤类型的样例包",
          "styles": [
            { "id": "test-caesar", "name": "凯撒 3", "category": "encoding",
              "note": "a→d b→e 的迷你凯撒",
              "steps": [ { "op": "mapReplace", "map": { "a": "d", "b": "e" } } ] },
            { "id": "test-wing", "name": "测试翅膀", "category": "decoration",
              "steps": [ { "op": "wrapString", "prefix": "꧁", "suffix": "꧂" } ] },
            { "id": "test-combo", "name": "组合管道", "category": "cjk-effect",
              "steps": [
                { "op": "useMap", "map": "bold" },
                { "op": "appendMark", "mark": "\u0488", "repeat": 2 },
                { "op": "ifChanged", "inner": { "op": "reverse" } }
              ] },
            { "id": "test-alg", "name": "算法引用", "category": "transform",
              "steps": [ { "op": "algorithm", "name": "rot13" } ] }
          ]
        }
        """;

        var parsed = StylePacks.ParseJson(packJson, "test.json");
        Check(parsed.Pack is not null, "样例包解析成功", string.Join("; ", parsed.Errors));
        if (parsed.Pack is not { } pack)
        {
            return;
        }

        Check(pack.Styles.Count == 4, "包内样式数");
        var styles = pack.Styles.Select(d => StyleFactory.FromDefinition(d, new StyleSource.Pack(pack.Name))).ToDictionary(s => s.Id);
        Check(styles["test-caesar"].Transform("ab") == "de", "mapReplace 内联映射生效");
        Check(styles["test-wing"].Transform("你好") == "꧁你好꧂", "wrapString 生效");
        Check(styles["test-combo"].Transform("a") == "𝐚\u0488\u0488", "组合管道（useMap+appendMark+守卫）生效");
        Check(styles["test-combo"].Transform("你好") == "好\u0488\u0488你\u0488\u0488", "组合管道守卫不短路（appendMark 已改变文本，字素倒序）");
        Check(styles["test-alg"].Transform("hello") == "uryyb", "algorithm 引用生效");
        Check(styles["test-caesar"].Source is StyleSource.Pack { PackName: "测试包" }, "来源标记为包");

        // 导出 → 序列化 → 解析 → 重编译 输出一致
        var exported = StylePacks.ExportStyles([Find("bold"), Find("wing-classic"), Find("spacing-wide")], "我的收藏", "我");
        var round = StylePacks.ParseJson(StylePacks.Serialize(exported), "roundtrip.json");
        Check(round.Pack is not null && round.Pack.Styles.Count == 3, "导出→解析往返成功", string.Join("; ", round.Errors));
        if (round.Pack is { } roundPack)
        {
            foreach (var def in roundPack.Styles)
            {
                var rebuilt = StyleFactory.FromDefinition(def);
                Check(rebuilt.Transform("Hello 你好 123") == Find(def.Id).Transform("Hello 你好 123"), $"往返输出一致：{def.Id}");
            }
        }

        // 坏输入：每条都必须被拦下（Pack 为 null 且 Errors 有内容）
        var badPacks = new (string Name, string Json)[]
        {
            ("孤立代理对", """{"schemaVersion":1,"name":"x","styles":[{"id":"a-1","name":"A","category":"encoding","steps":[{"op":"mapReplace","map":{"a":"\ud800"}}]}]}"""),
            ("控制字符值", """{"schemaVersion":1,"name":"x","styles":[{"id":"a-1","name":"A","category":"encoding","steps":[{"op":"mapReplace","map":{"a":"a\u0007b"}}]}]}"""),
            ("未知 op", """{"schemaVersion":1,"name":"x","styles":[{"id":"a-1","name":"A","category":"encoding","steps":[{"op":"explode"}]}]}"""),
            ("未知内置表", """{"schemaVersion":1,"name":"x","styles":[{"id":"a-1","name":"A","category":"encoding","steps":[{"op":"useMap","map":"nope"}]}]}"""),
            ("未知算法", """{"schemaVersion":1,"name":"x","styles":[{"id":"a-1","name":"A","category":"transform","steps":[{"op":"algorithm","name":"nope"}]}]}"""),
            ("未知分类", """{"schemaVersion":1,"name":"x","styles":[{"id":"a-1","name":"A","category":"fancy","steps":[{"op":"reverse"}]}]}"""),
            ("schemaVersion 不符", """{"schemaVersion":2,"name":"x","styles":[{"id":"a-1","name":"A","category":"encoding","steps":[{"op":"reverse"}]}]}"""),
            ("空样式表", """{"schemaVersion":1,"name":"x","styles":[]}"""),
            ("嵌套守卫", """{"schemaVersion":1,"name":"x","styles":[{"id":"a-1","name":"A","category":"encoding","steps":[{"op":"ifChanged","inner":{"op":"ifChanged","inner":{"op":"reverse"}}}]}]}"""),
            ("repeat 超限", """{"schemaVersion":1,"name":"x","styles":[{"id":"a-1","name":"A","category":"encoding","steps":[{"op":"appendMark","mark":"\u0488","repeat":9}]}]}"""),
            ("映射键多字符", """{"schemaVersion":1,"name":"x","styles":[{"id":"a-1","name":"A","category":"encoding","steps":[{"op":"mapReplace","map":{"ab":"c"}}]}]}"""),
            ("包内 ID 重复", """{"schemaVersion":1,"name":"x","styles":[{"id":"a-1","name":"A","category":"encoding","steps":[{"op":"reverse"}]},{"id":"a-1","name":"B","category":"encoding","steps":[{"op":"reverse"}]}]}"""),
            ("ID 格式非法", """{"schemaVersion":1,"name":"x","styles":[{"id":"Bad_Id","name":"A","category":"encoding","steps":[{"op":"reverse"}]}]}"""),
        };
        foreach (var (name, json) in badPacks)
        {
            var result = StylePacks.ParseJson(json, name + ".json");
            Check(result.Pack is null && result.Errors.Count > 0, $"拦截：{name}", string.Join("; ", result.Errors));
        }

        // 安装 / 扫描 / Reload / 冲突 / 卸载（临时目录）
        var tempDir = Path.Combine(Path.GetTempPath(), "fancytext-packs-test-" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        StylePacks.DirectoryOverrideForTests = tempDir;
        try
        {
            var sourcePath = Path.Combine(tempDir, "source-file.json");
            File.WriteAllText(sourcePath, packJson);
            var import = StylePacks.Import(sourcePath);
            Check(import.Success, "导入安装成功", string.Join("; ", import.Errors));
            Check(import.InstalledPath == Path.Combine(tempDir, "测试包.json"), "落盘为包名文件名", import.InstalledPath ?? "");

            var installed = StylePacks.LoadInstalled();
            Check(installed.Count == 1 && installed[0].PackName == "测试包" && installed[0].Styles.Count == 4
                && installed[0].Error is null, "扫描已安装包", string.Join("; ", installed.Select(i => i.Error)));

            StyleCatalog.Reload();
            Check(StyleCatalog.All.Count == StyleCatalog.BuiltInIds.Count + 4, "Reload 后包样式进入目录");
            Check(StyleCatalog.All.Any(s => s.Id == "test-caesar" && s.Source is StyleSource.Pack { PackName: "测试包" }), "包样式带来源标记");

            var conflictJson = """{"schemaVersion":1,"name":"冲突包","styles":[{"id":"bold","name":"冒充粗体","category":"latin-fancy","steps":[{"op":"reverse"}]}]}""";
            var conflictPath = Path.Combine(tempDir, "conflict.json");
            File.WriteAllText(conflictPath, conflictJson);
            var conflict = StylePacks.Import(conflictPath);
            Check(!conflict.Success && conflict.Errors.Count > 0, "与内置 ID 冲突被拒", string.Join("; ", conflict.Errors));

            Check(StylePacks.Remove("测试包"), "卸载成功");
            StyleCatalog.Reload();
            Check(StyleCatalog.All.All(s => s.Id != "test-caesar"), "卸载后 Reload 移除包样式");
        }
        finally
        {
            StylePacks.DirectoryOverrideForTests = null;
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        // 手动放置的坏包不拖累扫描
        var tempDir2 = Path.Combine(Path.GetTempPath(), "fancytext-packs-broken-" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir2);
        StylePacks.DirectoryOverrideForTests = tempDir2;
        try
        {
            File.WriteAllText(Path.Combine(tempDir2, "broken.json"), "{ 不是 JSON");
            File.WriteAllText(Path.Combine(tempDir2, "good.json"), packJson);
            var scan = StylePacks.LoadInstalled();
            Check(scan.Count == 2 && scan.Count(i => i.Error is not null) == 1 && scan.Count(i => i.Error is null) == 1,
                "坏包隔离（好包仍可加载）", string.Join("; ", scan.Select(i => $"{i.PackName}: {i.Error}")));
        }
        finally
        {
            StylePacks.DirectoryOverrideForTests = null;
            try
            {
                Directory.Delete(tempDir2, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static TextStyle Find(string id) =>
        StyleCatalog.All.FirstOrDefault(s => s.Id == id)
        ?? throw new InvalidOperationException($"找不到样式 {id}");
}
