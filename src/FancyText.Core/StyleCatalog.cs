using System.Text;

namespace FancyText.Core;

/// <summary>
/// 全部可用样式目录。每项都是「四个原语」之一的简单包装，或少数组合。
/// 样式调研来源见 docs/01-调研报告-花式文字插件.md。
/// 第二批扩充样式见 <see cref="StyleCatalog.Expanded.cs"/>（AddExpandedStyles）。
/// </summary>
public static partial class StyleCatalog
{
    public const string DefaultSample = "Hello 你好 123";

    public static IReadOnlyList<TextStyle> All { get; } = Build();

    /// <summary>第二批扩充样式（装饰模板/组合符预设/数字变体/编码/火星文还原/俄化体），实现见 StyleCatalog.Expanded.cs。</summary>
    private static partial void AddExpandedStyles(List<TextStyle> list);

    private static List<TextStyle> Build()
    {
        var list = new List<TextStyle>(100);

        // ================= 中文特效（组合附加符号，中英通吃） =================

        list.Add(new()
        {
            Id = "juhua-1", Name = "菊花体 ❶", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0488"),
            Note = "每字后附加 U+0488（组合西里尔数字符号，虚线圆圈环绕）",
        });
        list.Add(new()
        {
            Id = "juhua-2", Name = "菊花体 ❷（蚂蚁文）", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0489"),
            Note = "每字后附加 U+0489（Cyrillic millions sign，带点圆圈环绕）",
        });
        list.Add(new()
        {
            Id = "juhua-3", Name = "菊花体 ❸", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\uA670"),
            Note = "每字后附加 U+A670（组合西里尔十万标记；百万为 U+A671）",
        });
        list.Add(new()
        {
            Id = "juhua-4", Name = "菊花体 ❹", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\uA672"),
            Note = "每字后附加 U+A672（组合西里尔千万标记）",
        });

        list.Add(new()
        {
            Id = "strikethrough", Name = "删除线", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0336"),
            Note = "U+0336 组合长删除线",
        });
        list.Add(new()
        {
            Id = "strikethrough-double", Name = "双删除线", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0347"),
            Note = "U+0347 组合等号删除线",
        });
        list.Add(new()
        {
            Id = "strikethrough-slash", Name = "斜线贯穿", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0337"),
            Note = "U+0337 组合短斜线贯穿",
        });
        list.Add(new()
        {
            Id = "strikethrough-tilde", Name = "波浪删除线", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0334"),
            Note = "U+0334 组合波浪线贯穿",
        });
        list.Add(new()
        {
            Id = "underline", Name = "下划线", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0332"),
            Note = "U+0332 组合下划线",
        });
        list.Add(new()
        {
            Id = "underline-double", Name = "双下划线", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0333"),
            Note = "U+0333 组合双下划线",
        });
        list.Add(new()
        {
            Id = "underline-wavy", Name = "波浪下划线", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0330"),
            Note = "U+0330 组合波浪下划线",
        });
        list.Add(new()
        {
            Id = "overline", Name = "上划线", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0305"),
            Note = "U+0305 组合上划线",
        });
        list.Add(new()
        {
            Id = "overline-double", Name = "双上划线", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u033F"),
            Note = "U+033F 组合双上划线",
        });
        list.Add(new()
        {
            Id = "dot-above", Name = "顶点字", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0307"),
            Note = "U+0307 组合上加点",
        });
        list.Add(new()
        {
            Id = "ring-above", Name = "顶圈字", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u030A"),
            Note = "U+030A 组合上圆圈",
        });
        list.Add(new()
        {
            Id = "arrow-above", Name = "顶箭头字", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u20D7"),
            Note = "U+20D7 组合右上箭头",
        });

        list.Add(new()
        {
            Id = "enclose-circle", Name = "圈圈字", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u20DD"),
            Note = "U+20DD 组合包围圆（任意汉字加圈的通用方案，渲染依平台而定）",
        });
        list.Add(new()
        {
            Id = "enclose-square", Name = "包围框字", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u20DE"),
            Note = "U+20DE 组合包围方框",
        });
        list.Add(new()
        {
            Id = "enclose-diamond", Name = "菱形字", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u20DF"),
            Note = "U+20DF 组合包围菱形",
        });
        list.Add(new()
        {
            Id = "enclose-forbidden", Name = "禁止字", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u20E0"),
            Note = "U+20E0 组合禁止标志（圆圈加斜线）",
        });
        list.Add(new()
        {
            Id = "enclose-triangle", Name = "三角字", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u20E4"),
            Note = "U+20E4 组合上三角",
        });
        list.Add(new()
        {
            Id = "snowflake-above", Name = "雪花字", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u20F0"),
            Note = "U+20F0 组合上星号（雪花）",
        });

        list.Add(new()
        {
            Id = "bird-1", Name = "飞鸟文 ❶", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0F7C"),
            Note = "藏文元音符号 o（U+0F7C），形似小鸟落于字顶",
        });
        list.Add(new()
        {
            Id = "bird-2", Name = "飞鸟文 ❷", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0F7D"),
            Note = "藏文元音符号 OO（U+0F7D）",
        });
        list.Add(new()
        {
            Id = "butterfly", Name = "蝴蝶文", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0F72\u0F80"),
            Note = "藏文双元音符号（U+0F72 + U+0F80），形似蝴蝶",
        });
        list.Add(new()
        {
            Id = "tail", Name = "尾巴文", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0F18"),
            Note = "藏文声调标记（U+0F18）",
        });
        list.Add(new()
        {
            Id = "smoke", Name = "冒烟文", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0F82"),
            Note = "藏文声调（U+0F82），形似字顶冒烟",
        });
        list.Add(new()
        {
            Id = "smoke-thai", Name = "烟雾文 ❷", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0E49", repeat: 4),
            Note = "泰文声调标记 ้（U+0E49）× 4 堆叠",
        });

        list.Add(new()
        {
            Id = "sprout", Name = "萌芽文", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0E47\u0E49"),
            Note = "泰文 ็（U+0E47）+ ้（U+0E49），字顶冒出小芽",
        });
        list.Add(new()
        {
            Id = "heart-javanese", Name = "爱心文", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\uA9BF\u1B44"),
            Note = "爪哇文组合元音符 ꦿ（U+A9BF）+ 巴厘文组合符 ᭄（U+1B44）",
        });
        list.Add(new()
        {
            Id = "vine-1", Name = "花藤字 ❶", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.WrapString(TextTransforms.WrapEach(s, "ζั͡", ""), "", "✿"),
            Note = "ζ + 泰文 ั（U+0E31）+ 组合双倒弧 ͡（U+0361）拼成藤头，结尾缀 ✿",
        });
        list.Add(new()
        {
            Id = "vine-2", Name = "花藤字 ❷", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.WrapString(TextTransforms.AppendMark(s, "\uA9BF\u1B44"), "", "࿐"),
            Note = "每字后粘爪哇文 ꦿ + 巴厘文 ᭄，结尾缀藏文 ࿐",
        });
        list.Add(new()
        {
            Id = "hairpin", Name = "发卡文", Category = TextStyleCategory.CjkEffect,
            Transform = s => TextTransforms.AppendMark(s, "\u0749"),
            Note = "叙利亚文缩写符（U+0749），形似发卡",
        });
        list.Add(new()
        {
            Id = "zalgo-mini", Name = "魔鬼文字 · 轻", Category = TextStyleCategory.CjkEffect,
            Transform = s => ZalgoTransformer.Transform(s, ZalgoIntensity.Mini),
            Note = "Zalgo：每字随机叠加组合附加符号（U+0300–036F 池），轻度",
        });
        list.Add(new()
        {
            Id = "zalgo-normal", Name = "魔鬼文字 · 中", Category = TextStyleCategory.CjkEffect,
            Transform = s => ZalgoTransformer.Transform(s, ZalgoIntensity.Normal),
            Note = "Zalgo：每字随机叠加组合附加符号，中度",
        });
        list.Add(new()
        {
            Id = "zalgo-max", Name = "魔鬼文字 · 重", Category = TextStyleCategory.CjkEffect,
            Transform = s => ZalgoTransformer.Transform(s, ZalgoIntensity.Max),
            Note = "Zalgo：每字随机叠加组合附加符号，重度（可能撑破行高）",
        });

        // ================= 花体（Unicode 区段查表映射，仅拉丁字母与数字） =================

        list.Add(new()
        {
            Id = "bold", Name = "粗体 𝐁", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.Bold),
            Note = "数学粗体，U+1D400 / U+1D41A / U+1D7CE",
        });
        list.Add(new()
        {
            Id = "italic", Name = "斜体 𝐼", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.Italic),
            Note = "数学斜体，U+1D434 / U+1D44E（无数字）",
        });
        list.Add(new()
        {
            Id = "bold-italic", Name = "粗斜体 𝑯", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.BoldItalic),
            Note = "数学粗斜体，U+1D468 / U+1D482",
        });
        list.Add(new()
        {
            Id = "script", Name = "手写体 ℋ", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.Script),
            Note = "数学手写体，U+1D49C / U+1D4B6，洞字符在 Letterlike Symbols 区",
        });
        list.Add(new()
        {
            Id = "bold-script", Name = "花体 𝓗", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.BoldScript),
            Note = "数学粗手写体（最常见的「花体」），U+1D4D0 / U+1D4EA",
        });
        list.Add(new()
        {
            Id = "fraktur", Name = "哥特体 ℌ", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.Fraktur),
            Note = "Fraktur 哥特体 / Old English，U+1D504 / U+1D51E",
        });
        list.Add(new()
        {
            Id = "bold-fraktur", Name = "粗哥特体 𝕳", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.BoldFraktur),
            Note = "多数站点的「Old English」实为此体，U+1D56C / U+1D586",
        });
        list.Add(new()
        {
            Id = "double-struck", Name = "空心体 ℍ", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.DoubleStruck),
            Note = "双线体 Double-struck，U+1D538 / U+1D552 / U+1D7D8",
        });
        list.Add(new()
        {
            Id = "monospace", Name = "等宽体 𝙷", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.Monospace),
            Note = "数学等宽（打字机风），U+1D670 / U+1D68A / U+1D7F6",
        });
        list.Add(new()
        {
            Id = "sans", Name = "无衬线 𝖧", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.Sans),
            Note = "无衬线体，U+1D5A0 / U+1D5BA / U+1D7E2",
        });
        list.Add(new()
        {
            Id = "sans-bold", Name = "无衬线粗体 𝗛", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.SansBold),
            Note = "无衬线粗体，U+1D5D4 / U+1D5EE / U+1D7EC",
        });
        list.Add(new()
        {
            Id = "sans-italic", Name = "无衬线斜体 𝘏", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.SansItalic),
            Note = "无衬线斜体，U+1D608 / U+1D622",
        });
        list.Add(new()
        {
            Id = "sans-bold-italic", Name = "无衬线粗斜体 𝙃", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.SansBoldItalic),
            Note = "无衬线粗斜体，U+1D63C / U+1D656",
        });

        list.Add(new()
        {
            Id = "circled", Name = "泡泡字 ⓗ", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.Circled),
            Note = "带圈字母，大写 U+24B6 / 小写 U+24D0 / 数字 ①–⑨",
        });
        list.Add(new()
        {
            Id = "circled-negative", Name = "黑底圈字 🅗", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.CircledNegative),
            Note = "负片带圈字母 U+1F150–169，仅有大写（小写映射为同一字形）",
        });
        list.Add(new()
        {
            Id = "squared", Name = "方块字 🄷", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.Squared),
            Note = "负方块字母 U+1F130–149，仅有大写",
        });
        list.Add(new()
        {
            Id = "squared-negative", Name = "黑底方块字 🅷", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.SquaredNegative),
            Note = "负片方块字母 U+1F170–189，仅有大写",
        });
        list.Add(new()
        {
            Id = "parenthesized", Name = "括号字 ⒣", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.Parenthesized),
            Note = "带括号字母，大写 U+1F110 / 小写 U+249C / 数字 ⑴–⑼",
        });
        list.Add(new()
        {
            Id = "regional-indicator", Name = "旗帜字母 🇭", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.RegionalIndicator),
            Note = "Regional Indicator U+1F1E6–1F1FF，相邻两个字母在 Discord 等平台可能组合显示为国旗（任意字母对都可能成旗，如 com → 🇨🇴）",
        });

        list.Add(new()
        {
            Id = "fullwidth", Name = "全角 Ｈ（蒸汽波）", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.Fullwidth),
            Note = "半角转全角，ASCII → +0xFEE0，空格 → U+3000",
        });
        list.Add(new()
        {
            Id = "small-caps", Name = "小型大写 ʜ", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.SmallCaps),
            Note = "小型大写字母（语音扩展区），Q/X 无专用形（回退 ǫ/x）",
        });
        list.Add(new()
        {
            Id = "superscript", Name = "上标 ᴴ", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.Superscript),
            Note = "上标字母/数字（U+2070 区 + 修饰字母区），小写 q 无上标形",
        });
        list.Add(new()
        {
            Id = "subscript", Name = "下标 ₕ", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.Subscript),
            Note = "下标字母/数字（U+2080 区 + U+2090 区），多数辅音无下标形",
        });
        list.Add(new()
        {
            Id = "currency", Name = "货币体 ₲", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.Currency),
            Note = "货币符号形近字替换（instafonts/coolsymbol 表）",
        });
        list.Add(new()
        {
            Id = "symbols-mix", Name = "符号体 ђєll๏", Category = TextStyleCategory.LatinFancy,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.SymbolsMix),
            Note = "泰/希伯来/希腊/西里尔形近字混排（coolsymbol \"Symbols\" 表）",
        });

        // ================= 装饰（前后缀包围 / 逐字包围 / 分字） =================

        list.Add(new()
        {
            Id = "wing-classic", Name = "经典翅膀 ꧁꧂", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "꧁", "꧂"),
            Note = "爪哇文重迭括号 ꧁ U+A9C1 / ꧂ U+A9C2",
        });
        list.Add(new()
        {
            Id = "wing-fancy", Name = "华丽翅膀 ꧁༺༻꧂", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "꧁༺", "༻꧂"),
            Note = "爪哇文括号 + 藏文花括号 ༺ U+0F3A / ༻ U+0F3B",
        });
        list.Add(new()
        {
            Id = "wing-delicate", Name = "精美翅膀 ꧁༺๑༻꧂", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "꧁༺๑", "๑༻꧂"),
            Note = "泰文字符 ๑ U+0E51 点缀",
        });
        list.Add(new()
        {
            Id = "wing-crown", Name = "皇冠翅膀 ꧁♛꧂", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "꧁♛", "♛꧂"),
            Note = "「霸气体」即此类加皇冠/钻石的变体",
        });
        list.Add(new()
        {
            Id = "wing-flower", Name = "花朵翅膀 ꧁❀꧂", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "꧁❀", "❀꧂"),
            Note = "Dingbats 花朵 ❀ U+2740",
        });
        list.Add(new()
        {
            Id = "wing-mystic", Name = "神秘符号 ༺ཌༀད༻", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "༺ཌༀ", "ༀད༻"),
            Note = "藏文字母与符号组合（ཌ ༀ ད）",
        });
        list.Add(new()
        {
            Id = "wing-tibetan", Name = "古老印记 ༺ཌༀཉི༃ༀད༻", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "༺ཌༀཉི", "༃ༀད༻"),
            Note = "藏文全套装饰（ཉི ༃ 为日月元音记号）",
        });
        list.Add(new()
        {
            Id = "wing-angel", Name = "小翅膀 ʚɞ", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "ʚ", "ɞ"),
            Note = "亚美尼亚/格鲁吉亚形近字符",
        });
        list.Add(new()
        {
            Id = "wing-cloud", Name = "云朵边框 ☁", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "☁", "☁"),
            Note = "杂类符号云朵 U+2601",
        });
        list.Add(new()
        {
            Id = "wing-bow", Name = "蝴蝶结 ୨୧", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "୨୧", "୨୧"),
            Note = "奥里亚文数字字符 ୨୧",
        });
        list.Add(new()
        {
            Id = "border-star", Name = "星光边框 ★彡彡★", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "★彡", "彡★"),
            Note = "Dingbats 星与日文片假名 ミ 形符号彡",
        });
        list.Add(new()
        {
            Id = "border-aesthetic", Name = "花边框 ˜”*°•.", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "˜”*°•.", ".•°*”˜"),
            Note = "Aesthetic 花边（lingojam/fancytextpro 常见模板）",
        });
        list.Add(new()
        {
            Id = "border-cute", Name = "闪耀边框 ✧･ﾟ:", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "✧･ﾟ:", ":･ﾟ✧"),
            Note = "日系闪耀边框模板",
        });
        list.Add(new()
        {
            Id = "border-brackets-jp", Name = "日式括号 『』", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.WrapString(s, "『", "』"),
            Note = "CJK 标点直角双引号",
        });
        list.Add(new()
        {
            Id = "spacing-aesthetic", Name = "分字空格 H e l l o", Category = TextStyleCategory.Decoration,
            Transform = s => TextTransforms.Spacing(s, " "),
            Note = "字素间插空格（Aesthetic Spacing，中英通用：你 好）",
        });
        list.Add(new()
        {
            Id = "spacing-wide", Name = "宽体 Ｈ ｅ ｌ ｌ ｏ", Category = TextStyleCategory.Decoration,
            // 纯 CJK 输入时全角映射零命中，会退化成与「分字空格」相同的输出——直接视为不适用
            Transform = s =>
            {
                var fullwidth = TextTransforms.MapReplace(s, LatinMaps.Fullwidth);
                return string.Equals(fullwidth, s, StringComparison.Ordinal)
                    ? s
                    : TextTransforms.Spacing(fullwidth, " ");
            },
            Note = "全角 + 空格（Wide），仅对拉丁字母/数字/半角标点生效",
        });

        // ================= 变换（拼写/方向） =================

        list.Add(new()
        {
            Id = "upside-down", Name = "倒转文字 oʇʇǝ", Category = TextStyleCategory.Transform,
            Transform = s => TextTransforms.MapAndReverse(s, LatinMaps.UpsideDown),
            Note = "先按倒转映射表替换（fileformat.info 表）再整体倒序",
        });
        list.Add(new()
        {
            Id = "mirror", Name = "镜像文字 oꞁꞁɘ", Category = TextStyleCategory.Transform,
            Transform = s => TextTransforms.MapAndReverse(s, LatinMaps.Mirror),
            Note = "先按镜像映射表替换再整体倒序（smalltextgen 表）",
        });
        list.Add(new()
        {
            Id = "reverse", Name = "倒序 olleH", Category = TextStyleCategory.Transform,
            Transform = TextTransforms.Reverse,
            Note = "按字素倒序（代理对与组合符不拆散）",
        });
        list.Add(new()
        {
            Id = "leet", Name = "Leet 语 H3LL0", Category = TextStyleCategory.Transform,
            Transform = s => TextTransforms.MapReplace(s, LatinMaps.Leet),
            Note = "基础级 leetspeak：a→4 e→3 i→1 o→0 s→5 t→7 b→8 g→9 l→1",
        });
        list.Add(new()
        {
            Id = "strip-marks", Name = "还原（去装饰）", Category = TextStyleCategory.Transform,
            Transform = TextTransforms.StripCombiningMarks,
            Note = "去掉本插件生成的全部组合附加符号，还原原文",
        });

        // ================= 中文 =================

        list.Add(new()
        {
            Id = "martian", Name = "火星文", Category = TextStyleCategory.Chinese,
            Transform = s => TextTransforms.MapReplace(s, MartianDictionary.Map),
            Note = $"简体 → 火星文异体字字典逐字替换（cnchar MIT 字典，{MartianDictionary.EntryCount} 字）",
        });

        // ================= 编码 =================

        list.Add(new()
        {
            Id = "base64", Name = "Base64", Category = TextStyleCategory.Encoding,
            Transform = s => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)),
            Note = "UTF-8 字节 → Base64",
        });
        list.Add(new()
        {
            Id = "rot13", Name = "ROT13", Category = TextStyleCategory.Encoding,
            Transform = LatinMaps.Rot13,
            Note = "字母移位 13（两次变换还原）",
        });
        list.Add(new()
        {
            Id = "morse", Name = "摩斯电码", Category = TextStyleCategory.Encoding,
            Transform = LatinMaps.ToMorse,
            Note = "字母/数字 → 摩斯电码，词间用 / 分隔",
        });

        // ================= 第二批扩充（见 StyleCatalog.Expanded.cs） =================

        AddExpandedStyles(list);

        return list;
    }
}
