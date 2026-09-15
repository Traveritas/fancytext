namespace FancyText.Core;

/// <summary>
/// 全部可用样式目录：内置声明式定义（本文件 + StyleCatalog.Expanded.cs）经 <see cref="StyleFactory"/> 编译。
/// 每项都是「声明式步骤」的简单管道或少数组合；样式调研来源见 docs/01-调研报告-花式文字插件.md。
/// 步骤语义见 <see cref="TransformStep"/>；映射表按名引用共享（见 KnownTransforms）。
/// </summary>
public static partial class StyleCatalog
{
    public const string DefaultSample = "Hello 你好 123";

    private static readonly object Gate = new();
    private static List<TextStyle>? _builtIn;
    private static IReadOnlyList<TextStyle> _all = BuildAll();

    /// <summary>全部样式：内置 + 已安装样式包（%LOCALAPPDATA%\FancyText\styles）。进程内缓存，导入/卸载后调 <see cref="Reload"/>。</summary>
    public static IReadOnlyList<TextStyle> All
    {
        get { lock (Gate) { return _all; } }
    }

    /// <summary>仅内置样式（声明式定义编译，进程内构建一次）。</summary>
    private static List<TextStyle> BuiltIn => _builtIn ??= Build();

    /// <summary>仅内置样式的稳定 ID 集（包冲突检查用，不受包增删影响）。</summary>
    public static IReadOnlyCollection<string> BuiltInIds { get; } = BuiltIn.Select(s => s.Id).ToArray();

    /// <summary>重建 All（内置 + 扫描包目录）。导入/卸载样式包后调用；UI 需自行刷新缓存的索引。</summary>
    public static void Reload()
    {
        lock (Gate)
        {
            _all = BuildAll();
        }
    }

    private static List<TextStyle> BuildAll()
    {
        var builtIn = BuiltIn;
        var list = new List<TextStyle>(builtIn.Count);
        list.AddRange(builtIn);
        list.AddRange(StylePacks.LoadInstalledStyles(builtIn.Select(s => s.Id).ToArray()));
        return list;
    }

    /// <summary>第二批扩充样式（装饰模板/组合符预设/数字变体/编码/火星文还原/俄化体），定义见 StyleCatalog.Expanded.cs。</summary>
    private static partial void AddExpandedStyles(List<StyleDefinition> list);

    private static List<TextStyle> Build()
    {
        var list = new List<StyleDefinition>(100);

        // ================= 特效（组合附加符号，中英通吃） =================

        list.Add(new StyleDefinition
        {
            Id = "juhua-1", Name = "菊花体 ❶", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0488")],
            Note = "每字后附加 U+0488（组合西里尔数字符号，虚线圆圈环绕）",
        });
        list.Add(new StyleDefinition
        {
            Id = "juhua-2", Name = "菊花体 ❷（蚂蚁文）", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0489")],
            Note = "每字后附加 U+0489（Cyrillic millions sign，带点圆圈环绕）",
        });
        list.Add(new StyleDefinition
        {
            Id = "juhua-3", Name = "菊花体 ❸", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\uA670")],
            Note = "每字后附加 U+A670（组合西里尔十万标记；百万为 U+A671）",
        });
        list.Add(new StyleDefinition
        {
            Id = "juhua-4", Name = "菊花体 ❹", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\uA672")],
            Note = "每字后附加 U+A672（组合西里尔千万标记）",
        });

        list.Add(new StyleDefinition
        {
            Id = "strikethrough", Name = "删除线", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0336")],
            Note = "U+0336 组合长删除线",
        });
        list.Add(new StyleDefinition
        {
            Id = "strikethrough-double", Name = "双删除线", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0347")],
            Note = "U+0347 组合等号删除线",
        });
        list.Add(new StyleDefinition
        {
            Id = "strikethrough-slash", Name = "斜线贯穿", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0337")],
            Note = "U+0337 组合短斜线贯穿",
        });
        list.Add(new StyleDefinition
        {
            Id = "strikethrough-tilde", Name = "波浪删除线", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0334")],
            Note = "U+0334 组合波浪线贯穿",
        });
        list.Add(new StyleDefinition
        {
            Id = "underline", Name = "下划线", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0332")],
            Note = "U+0332 组合下划线",
        });
        list.Add(new StyleDefinition
        {
            Id = "underline-double", Name = "双下划线", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0333")],
            Note = "U+0333 组合双下划线",
        });
        list.Add(new StyleDefinition
        {
            Id = "underline-wavy", Name = "波浪下划线", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0330")],
            Note = "U+0330 组合波浪下划线",
        });
        list.Add(new StyleDefinition
        {
            Id = "overline", Name = "上划线", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0305")],
            Note = "U+0305 组合上划线",
        });
        list.Add(new StyleDefinition
        {
            Id = "overline-double", Name = "双上划线", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u033F")],
            Note = "U+033F 组合双上划线",
        });
        list.Add(new StyleDefinition
        {
            Id = "dot-above", Name = "顶点字", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0307")],
            Note = "U+0307 组合上加点",
        });
        list.Add(new StyleDefinition
        {
            Id = "ring-above", Name = "顶圈字", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u030A")],
            Note = "U+030A 组合上圆圈",
        });
        list.Add(new StyleDefinition
        {
            Id = "arrow-above", Name = "顶箭头字", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u20D7")],
            Note = "U+20D7 组合右上箭头",
        });

        list.Add(new StyleDefinition
        {
            Id = "enclose-circle", Name = "圈圈字", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u20DD")],
            Note = "U+20DD 组合包围圆（任意汉字加圈的通用方案，渲染依平台而定）",
        });
        list.Add(new StyleDefinition
        {
            Id = "enclose-square", Name = "包围框字", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u20DE")],
            Note = "U+20DE 组合包围方框",
        });
        list.Add(new StyleDefinition
        {
            Id = "enclose-diamond", Name = "菱形字", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u20DF")],
            Note = "U+20DF 组合包围菱形",
        });
        list.Add(new StyleDefinition
        {
            Id = "enclose-forbidden", Name = "禁止字", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u20E0")],
            Note = "U+20E0 组合禁止标志（圆圈加斜线）",
        });
        list.Add(new StyleDefinition
        {
            Id = "enclose-triangle", Name = "三角字", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u20E4")],
            Note = "U+20E4 组合上三角",
        });
        list.Add(new StyleDefinition
        {
            Id = "snowflake-above", Name = "雪花字", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u20F0")],
            Note = "U+20F0 组合上星号（雪花）",
        });

        list.Add(new StyleDefinition
        {
            Id = "bird-1", Name = "飞鸟文 ❶", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0F7C")],
            Note = "藏文元音符号 o（U+0F7C），形似小鸟落于字顶",
        });
        list.Add(new StyleDefinition
        {
            Id = "bird-2", Name = "飞鸟文 ❷", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0F7D")],
            Note = "藏文元音符号 OO（U+0F7D）",
        });
        list.Add(new StyleDefinition
        {
            Id = "butterfly", Name = "蝴蝶文", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0F72\u0F80")],
            Note = "藏文双元音符号（U+0F72 + U+0F80），形似蝴蝶",
        });
        list.Add(new StyleDefinition
        {
            Id = "tail", Name = "尾巴文", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0F18")],
            Note = "藏文声调标记（U+0F18）",
        });
        list.Add(new StyleDefinition
        {
            Id = "smoke", Name = "冒烟文", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0F82")],
            Note = "藏文声调（U+0F82），形似字顶冒烟",
        });
        list.Add(new StyleDefinition
        {
            Id = "smoke-thai", Name = "烟雾文 ❷", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0E49", 4)],
            Note = "泰文声调标记 ้（U+0E49）× 4 堆叠",
        });

        list.Add(new StyleDefinition
        {
            Id = "sprout", Name = "萌芽文", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0E47\u0E49")],
            Note = "泰文 ็（U+0E47）+ ้（U+0E49），字顶冒出小芽",
        });
        list.Add(new StyleDefinition
        {
            Id = "heart-javanese", Name = "爱心文", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\uA9BF\u1B44")],
            Note = "爪哇文组合元音符 ꦿ（U+A9BF）+ 巴厘文组合符 ᭄（U+1B44）",
        });
        list.Add(new StyleDefinition
        {
            Id = "vine-1", Name = "花藤字 ❶", Category = TextStyleCategory.CjkEffect,
            Steps = [new WrapEachStep("ζั͡", ""), new WrapStringStep("", "✿")],
            Note = "ζ + 泰文 ั（U+0E31）+ 组合双倒弧 ͡（U+0361）拼成藤头，结尾缀 ✿",
        });
        list.Add(new StyleDefinition
        {
            Id = "vine-2", Name = "花藤字 ❷", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\uA9BF\u1B44"), new WrapStringStep("", "࿐")],
            Note = "每字后粘爪哇文 ꦿ + 巴厘文 ᭄，结尾缀藏文 ༐",
        });
        list.Add(new StyleDefinition
        {
            Id = "hairpin", Name = "发卡文", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0749")],
            Note = "叙利亚文缩写符（U+0749），形似发卡",
        });
        list.Add(new StyleDefinition
        {
            Id = "zalgo-mini", Name = "魔鬼文字 · 轻", Category = TextStyleCategory.CjkEffect,
            Steps = [new AlgorithmStep(KnownAlgorithm.ZalgoMini)],
            Note = "Zalgo：每字随机叠加组合附加符号（U+0300–036F 池），轻度",
        });
        list.Add(new StyleDefinition
        {
            Id = "zalgo-normal", Name = "魔鬼文字 · 中", Category = TextStyleCategory.CjkEffect,
            Steps = [new AlgorithmStep(KnownAlgorithm.ZalgoNormal)],
            Note = "Zalgo：每字随机叠加组合附加符号，中度",
        });
        list.Add(new StyleDefinition
        {
            Id = "zalgo-max", Name = "魔鬼文字 · 重", Category = TextStyleCategory.CjkEffect,
            Steps = [new AlgorithmStep(KnownAlgorithm.ZalgoMax)],
            Note = "Zalgo：每字随机叠加组合附加符号，重度（可能撑破行高）",
        });

        // ================= 英文/字母花体（Unicode 区段查表映射，仅拉丁字母与数字） =================

        list.Add(new StyleDefinition
        {
            Id = "bold", Name = "粗体 𝐁", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("bold")],
            Note = "数学粗体，U+1D400 / U+1D41A / U+1D7CE",
        });
        list.Add(new StyleDefinition
        {
            Id = "italic", Name = "斜体 𝐼", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("italic")],
            Note = "数学斜体，U+1D434 / U+1D44E（无数字）",
        });
        list.Add(new StyleDefinition
        {
            Id = "bold-italic", Name = "粗斜体 𝑯", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("bold-italic")],
            Note = "数学粗斜体，U+1D468 / U+1D482",
        });
        list.Add(new StyleDefinition
        {
            Id = "script", Name = "手写体 ℋ", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("script")],
            Note = "数学手写体，U+1D49C / U+1D4B6，洞字符在 Letterlike Symbols 区",
        });
        list.Add(new StyleDefinition
        {
            Id = "bold-script", Name = "花体 𝓗", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("bold-script")],
            Note = "数学粗手写体（最常见的「花体」），U+1D4D0 / U+1D4EA",
        });
        list.Add(new StyleDefinition
        {
            Id = "fraktur", Name = "哥特体 ℌ", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("fraktur")],
            Note = "Fraktur 哥特体 / Old English，U+1D504 / U+1D51E",
        });
        list.Add(new StyleDefinition
        {
            Id = "bold-fraktur", Name = "粗哥特体 𝕳", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("bold-fraktur")],
            Note = "多数站点的「Old English」实为此体，U+1D56C / U+1D586",
        });
        list.Add(new StyleDefinition
        {
            Id = "double-struck", Name = "空心体 ℍ", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("double-struck")],
            Note = "双线体 Double-struck，U+1D538 / U+1D552 / U+1D7D8",
        });
        list.Add(new StyleDefinition
        {
            Id = "monospace", Name = "等宽体 𝙷", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("monospace")],
            Note = "数学等宽（打字机风），U+1D670 / U+1D68A / U+1D7F6",
        });
        list.Add(new StyleDefinition
        {
            Id = "sans", Name = "无衬线 𝖧", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("sans")],
            Note = "无衬线体，U+1D5A0 / U+1D5BA / U+1D7E2",
        });
        list.Add(new StyleDefinition
        {
            Id = "sans-bold", Name = "无衬线粗体 𝗛", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("sans-bold")],
            Note = "无衬线粗体，U+1D5D4 / U+1D5EE / U+1D7EC",
        });
        list.Add(new StyleDefinition
        {
            Id = "sans-italic", Name = "无衬线斜体 𝘏", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("sans-italic")],
            Note = "无衬线斜体，U+1D608 / U+1D622",
        });
        list.Add(new StyleDefinition
        {
            Id = "sans-bold-italic", Name = "无衬线粗斜体 𝙃", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("sans-bold-italic")],
            Note = "无衬线粗斜体，U+1D63C / U+1D656",
        });

        list.Add(new StyleDefinition
        {
            Id = "circled", Name = "泡泡字 ⓗ", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("circled")],
            Note = "带圈字母，大写 U+24B6 / 小写 U+24D0 / 数字 ①–⑨",
        });
        list.Add(new StyleDefinition
        {
            Id = "circled-negative", Name = "黑底圈字 🅗", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("circled-negative")],
            Note = "负片带圈字母 U+1F150–169，仅有大写（小写映射为同一字形）",
        });
        list.Add(new StyleDefinition
        {
            Id = "squared", Name = "方块字 🄷", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("squared")],
            Note = "负方块字母 U+1F130–149，仅有大写",
        });
        list.Add(new StyleDefinition
        {
            Id = "squared-negative", Name = "黑底方块字 🅷", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("squared-negative")],
            Note = "负片方块字母 U+1F170–189，仅有大写",
        });
        list.Add(new StyleDefinition
        {
            Id = "parenthesized", Name = "括号字 ⒣", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("parenthesized")],
            Note = "带括号字母，大写 U+1F110 / 小写 U+249C / 数字 ⑴–⑼",
        });
        list.Add(new StyleDefinition
        {
            Id = "regional-indicator", Name = "旗帜字母 🇭", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("regional-indicator")],
            Note = "Regional Indicator U+1F1E6–1F1FF，相邻两个字母在 Discord 等平台可能组合显示为国旗（任意字母对都可能成旗，如 com → 🇨🇴）",
        });

        list.Add(new StyleDefinition
        {
            Id = "fullwidth", Name = "全角 Ｈ（蒸汽波）", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("fullwidth")],
            Note = "半角转全角，ASCII → +0xFEE0，空格 → U+3000",
        });
        list.Add(new StyleDefinition
        {
            Id = "small-caps", Name = "小型大写 ʜ", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("small-caps")],
            Note = "小型大写字母（语音扩展区），Q/X 无专用形（回退 ǫ/x）",
        });
        list.Add(new StyleDefinition
        {
            Id = "superscript", Name = "上标 ᴴ", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("superscript")],
            Note = "上标字母/数字（U+2070 区 + 修饰字母区），小写 q 无上标形",
        });
        list.Add(new StyleDefinition
        {
            Id = "subscript", Name = "下标 ₕ", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("subscript")],
            Note = "下标字母/数字（U+2080 区 + U+2090 区），多数辅音无下标形",
        });
        list.Add(new StyleDefinition
        {
            Id = "currency", Name = "货币体 ₲", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("currency")],
            Note = "货币符号形近字替换（instafonts/coolsymbol 表）",
        });
        list.Add(new StyleDefinition
        {
            Id = "symbols-mix", Name = "符号体 ђєll๏", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("symbols-mix")],
            Note = "泰/希伯来/希腊/西里尔形近字混排（coolsymbol \"Symbols\" 表）",
        });

        // ================= 装饰（前后缀包围 / 逐字包围 / 分字） =================

        list.Add(new StyleDefinition
        {
            Id = "wing-classic", Name = "经典翅膀 ꧁꧂", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("꧁", "꧂")],
            Note = "爪哇文重迭括号 ꧁ U+A9C1 / ꧂ U+A9C2",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-fancy", Name = "华丽翅膀 ꧁༺༻꧂", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("꧁༺", "༻꧂")],
            Note = "爪哇文括号 + 藏文花括号 ༺ U+0F3A / ༻ U+0F3B",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-delicate", Name = "精美翅膀 ꧁༺๑༻꧂", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("꧁༺๑", "๑༻꧂")],
            Note = "泰文字符 ๑ U+0E51 点缀",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-crown", Name = "皇冠翅膀 ꧁♛꧂", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("꧁♛", "♛꧂")],
            Note = "「霸气体」即此类加皇冠/钻石的变体",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-flower", Name = "花朵翅膀 ꧁❀꧂", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("꧁❀", "❀꧂")],
            Note = "Dingbats 花朵 ❀ U+2740",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-mystic", Name = "神秘符号 ༺ཌༀད༻", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("༺ཌༀ", "ༀད༻")],
            Note = "藏文字母与符号组合（ཌ ༀ ད）",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-tibetan", Name = "古老印记 ༺ཌༀཉི༃ༀད༻", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("༺ཌༀཉི", "༃ༀད༻")],
            Note = "藏文全套装饰（ཉི ༃ 为日月元音记号）",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-angel", Name = "小翅膀 ʚɞ", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("ʚ", "ɞ")],
            Note = "亚美尼亚/格鲁吉亚形近字符",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-cloud", Name = "云朵边框 ☁", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("☁", "☁")],
            Note = "杂类符号云朵 U+2601",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-bow", Name = "蝴蝶结 ୨୧", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("୨୧", "୨୧")],
            Note = "奥里亚文数字字符 ୨୧",
        });
        list.Add(new StyleDefinition
        {
            Id = "border-star", Name = "星光边框 ★彡彡★", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("★彡", "彡★")],
            Note = "Dingbats 星与日文片假名 ミ 形符号彡",
        });
        list.Add(new StyleDefinition
        {
            Id = "border-aesthetic", Name = "花边框 ˜”*°•.", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("˜”*°•.", ".•°*”˜")],
            Note = "Aesthetic 花边（lingojam/fancytextpro 常见模板）",
        });
        list.Add(new StyleDefinition
        {
            Id = "border-cute", Name = "闪耀边框 ✧･ﾟ:", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("✧･ﾟ:", ":･ﾟ✧")],
            Note = "日系闪耀边框模板",
        });
        list.Add(new StyleDefinition
        {
            Id = "border-brackets-jp", Name = "日式括号 『』", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("『", "』")],
            Note = "CJK 标点直角双引号",
        });
        list.Add(new StyleDefinition
        {
            Id = "spacing-aesthetic", Name = "分字空格 H e l l o", Category = TextStyleCategory.Decoration,
            Steps = [new SpacingStep(" ")],
            Note = "字素间插空格（Aesthetic Spacing，中英通用：你 好）",
        });
        list.Add(new StyleDefinition
        {
            Id = "spacing-wide", Name = "宽体 Ｈ ｅ ｌ ｌ ｏ", Category = TextStyleCategory.Decoration,
            // 纯 CJK 输入时全角映射零命中，守卫短路返回原文——与分字空格区分，视为不适用
            Steps = [new UseMapStep("fullwidth"), new IfChangedStep(new SpacingStep(" "))],
            Note = "全角 + 空格（Wide），仅对拉丁字母/数字/半角标点生效",
        });

        // ================= 变换（拼写/方向） =================

        list.Add(new StyleDefinition
        {
            Id = "upside-down", Name = "倒转文字 oʇʇǝ", Category = TextStyleCategory.Transform,
            Steps = [new UseMapStep("upside-down"), new IfChangedStep(new ReverseStep())],
            Note = "先按倒转映射表替换（fileformat.info 表）再整体倒序",
        });
        list.Add(new StyleDefinition
        {
            Id = "mirror", Name = "镜像文字 oꞁꞁɘ", Category = TextStyleCategory.Transform,
            Steps = [new UseMapStep("mirror"), new IfChangedStep(new ReverseStep())],
            Note = "先按镜像映射表替换再整体倒序（smalltextgen 表）",
        });
        list.Add(new StyleDefinition
        {
            Id = "reverse", Name = "倒序 olleH", Category = TextStyleCategory.Transform,
            Steps = [new ReverseStep()],
            Note = "按字素倒序（代理对与组合符不拆散）",
        });
        list.Add(new StyleDefinition
        {
            Id = "leet", Name = "Leet 语 H3LL0", Category = TextStyleCategory.Transform,
            Steps = [new UseMapStep("leet")],
            Note = "基础级 leetspeak：a→4 e→3 i→1 o→0 s→5 t→7 b→8 g→9 l→1",
        });
        list.Add(new StyleDefinition
        {
            Id = "strip-marks", Name = "还原（去装饰）", Category = TextStyleCategory.Transform,
            Steps = [new AlgorithmStep(KnownAlgorithm.StripCombiningMarks)],
            Note = "去掉本插件生成的全部组合附加符号，还原原文",
        });

        // ================= 中文 =================

        list.Add(new StyleDefinition
        {
            Id = "martian", Name = "火星文", Category = TextStyleCategory.Chinese,
            Steps = [new UseMapStep("martian")],
            Note = $"简体 → 火星文异体字字典逐字替换（cnchar MIT 字典，{MartianDictionary.EntryCount} 字）",
        });

        // ================= 编码 =================

        list.Add(new StyleDefinition
        {
            Id = "base64", Name = "Base64", Category = TextStyleCategory.Encoding,
            Steps = [new AlgorithmStep(KnownAlgorithm.Base64)],
            Note = "UTF-8 字节 → Base64",
        });
        list.Add(new StyleDefinition
        {
            Id = "rot13", Name = "ROT13", Category = TextStyleCategory.Encoding,
            Steps = [new AlgorithmStep(KnownAlgorithm.Rot13)],
            Note = "字母移位 13（两次变换还原）",
        });
        list.Add(new StyleDefinition
        {
            Id = "morse", Name = "摩斯电码", Category = TextStyleCategory.Encoding,
            Steps = [new AlgorithmStep(KnownAlgorithm.Morse)],
            Note = "字母/数字 → 摩斯电码，词间用 / 分隔",
        });

        // ================= 第二批扩充（见 StyleCatalog.Expanded.cs） =================

        AddExpandedStyles(list);

        return list.Select(ApplyEnglish).Select(d => StyleFactory.FromDefinition(d)).ToList();
    }

    /// <summary>给内置定义叠加英文层（StyleCatalog.English.cs 的名称/机制说明表；包样式自带 nameEn/noteEn）。</summary>
    private static StyleDefinition ApplyEnglish(StyleDefinition definition) => definition with
    {
        NameEn = definition.NameEn ?? StyleEnglish.Names.GetValueOrDefault(definition.Id),
        NoteEn = definition.NoteEn ?? StyleEnglish.Notes.GetValueOrDefault(definition.Id),
    };
}
