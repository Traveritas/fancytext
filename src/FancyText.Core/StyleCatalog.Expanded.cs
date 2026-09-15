namespace FancyText.Core;

/// <summary>
/// <see cref="StyleCatalog"/> 的第二批扩充样式：
/// 装饰模板（2.C1/2.C3）、组合符预设（2.B2）、数字变体（2.A6）、编码（2.E）、火星文还原（2.A5 反向）、俄化体。
/// 码点均取自 docs/01-调研报告-花式文字插件.md 对应小节的实测数据。
/// 组合附加符号一律写 \uXXXX 转义；模板中易混淆的藏文标记亦用转义，常见花符号沿用 wing- 系列的字面量写法。
/// 数字/俄化/火星文反向表与编码算法见 KnownTransforms / EncodingTransforms。
/// </summary>
public static partial class StyleCatalog
{
    private static partial void AddExpandedStyles(List<StyleDefinition> list)
    {
        // ================= 组合符预设（调研报告 2.B2，中英通吃） =================

        list.Add(new StyleDefinition
        {
            Id = "paren-marks", Name = "连弧文 ͜͡", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u035C\u0361")],
            Note = "每字后附加 U+035C（组合双短音符下）+ U+0361（组合双倒弧），渲染成括号夹字",
        });
        list.Add(new StyleDefinition
        {
            Id = "stripes", Name = "横条纹字 ͟͞", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u035E\u035F")],
            Note = "每字后附加 U+035E（组合双长音符上）+ U+035F（组合双长音符下），上下夹出条纹",
        });
        list.Add(new StyleDefinition
        {
            Id = "lightning", Name = "闪电文 ͛", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u035B")],
            Note = "每字后附加 U+035B（组合闪电形符号，coolsymbol \"Lightning/Zigzag\" 预设）",
        });
        list.Add(new StyleDefinition
        {
            Id = "wave-head", Name = "角头字 ̚", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u031A")],
            Note = "每字后附加 U+031A（组合左角号上，调研报告 2.B2「波浪头」）",
        });
        list.Add(new StyleDefinition
        {
            Id = "x-above", Name = "叉上字 ̽", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u033D")],
            Note = "每字后附加 U+033D（组合叉号上）",
        });
        list.Add(new StyleDefinition
        {
            Id = "dot-below", Name = "点下字 ̣", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0323")],
            Note = "每字后附加 U+0323（组合下加点，调研报告 2.B2「点下」）",
        });
        list.Add(new StyleDefinition
        {
            Id = "manipuri-underline", Name = "曼尼普尔下划线 ꯭", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\uAADF")],
            Note = "每字后附加 U+AADF（曼尼普尔文组合标记，形似下划线）",
        });
        list.Add(new StyleDefinition
        {
            Id = "lace", Name = "花边文 ஊ", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0B8A")],
            Note = "每字后附加 U+0B8A（印度系文字区字符 ஊ，调研报告 2.B5「花边文」）",
        });
        list.Add(new StyleDefinition
        {
            Id = "grass", Name = "草头文 ෴", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u0DF4")],
            Note = "每字后附加 U+0DF4（僧伽罗文 Kunḍaliya 花饰符 ෴）",
        });
        list.Add(new StyleDefinition
        {
            Id = "smoke-arabic", Name = "烟雾文 ❸", Category = TextStyleCategory.CjkEffect,
            Steps = [new AppendMarkStep("\u06E3", 7)],
            Note = "每字后附加阿拉伯文小型低形符 U+06E3 × 7 堆叠（调研报告 2.B4「冒烟文③」）",
        });

        // ================= 装饰模板（调研报告 2.C1 翅膀/括号对 + 2.C3 边框） =================

        list.Add(new StyleDefinition
        {
            Id = "wing-elegant", Name = "典雅翅膀 ꧁❦༺", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("꧁❦༺", "༻❦꧂")],
            Note = "爪哇文括号 ꧁꧂ + 花形心符 ❦ U+2766 + 藏文花括号 ༺༻（2.C1「典雅」模板）",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-diamond", Name = "钻石翅膀 ꧁༺◇", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("꧁༺◇", "◇༻꧂")],
            Note = "白菱形 ◇ U+25C7 点缀（2.C1「钻石」模板，「霸气体」变体）",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-double", Name = "双层翅膀 ꧁༺༒", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("꧁༺\u0F12", "\u0F12༻꧂")],
            Note = "藏文 RDEL NAG RDEL DKAR ༒ U+0F12 点缀（2.C1「双层」模板）",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-shimmer", Name = "流光翅膀 ꧁༺✦", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("꧁༺✦", "✦༻꧂")],
            Note = "黑四角星 ✦ U+2726 点缀（2.C1「流光」模板）",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-soft", Name = "柔光翅膀 ꧁༺❁", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("꧁༺❁", "❁༻꧂")],
            Note = "重型花饰符 ❁ U+2741 点缀（2.C1「柔光」模板）",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-sanskrit", Name = "藏文花结 ༺࿈", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("༺\u0FC8", "\u0FC8༻")],
            Note = "藏文符号却丹菩提座 ࿈ U+0FC8（2.C1 神秘符号系）",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-tibetan-ornament", Name = "藏式花纹 ༺ཌ༈", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("༺\u0F4C\u0F08", "\u0F08\u0F51༻")],
            Note = "藏文字母 ཌ U+0F4C、标记 ༈ U+0F08、ད U+0F51 组合的藏式花纹模板",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-charm", Name = "护符边框 ༄༊", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("\u0F04\u0F0A", "\u0F7C\u0F82\u0F7E\u0FC6\u0FD0")],
            Note = "藏文起始符 ༄ U+0F04 + 信笺符 ༊ U+0F0A；后缀 ་元音/声调/花饰串 ོྂཾ࿆࿐（U+0F7C/82/7E/FC6/FD0）",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-heart", Name = "甜心括号 ෆ", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("ෆ", "ෆ")],
            Note = "僧伽罗文修饰符 ෆ U+0DC6，形似甜心括号（2.C1 可爱/古风系）",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-bunny", Name = "兔系装饰 ₍ᐢ", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("₍ᐢ", "ᐢ₎")],
            Note = "下标括号 ₍ U+208D/₎ U+208E + 加拿大原住民音节 ᐢ U+1422（2.C1 可爱系颜文字模板）",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-moonlight", Name = "月华装饰 ☾༺", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("☾༺", "༻☽")],
            Note = "上弦月 ☾ U+263E / 下弦月 ☽ U+263D + 藏文花括号 ༺༻（2.C1 模板）",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-starmoon", Name = "星月装饰 ☾✦", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("☾✦", "✦☽")],
            Note = "月牙 U+263E/U+263D + 黑四角星 ✦ U+2726（2.C1 模板）",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-asterism", Name = "星点装饰 ⁂", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("⁂", "⁂")],
            Note = "三星符 ⁂ U+2042（2.C1 模板）",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-retro", Name = "复古花纹 ꧁༺༽༾ཊ", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("꧁༺\u0F3D\u0F3E\u0F4A", "\u0F4F\u0F3F\u0F3C༻꧂")],
            Note = "藏文括号组 ༼ U+0F3C/༽ U+0F3D/༾ U+0F3E/༿ U+0F3F 与字母 ཊ U+0F4A/ཏ U+0F4F 拼成的复古花纹",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-dots", Name = "圆点边框 ｡o○", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("｡o○", "○o｡")],
            Note = "半角句号 ｡ U+FF61 + 白圆 ○ U+25CB 组成的日系边框（2.C1 模板）",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-nested", Name = "双层括号 ༼༺", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("\u0F3C༺", "༻\u0F3D")],
            Note = "藏文括号 ༼ U+0F3C / ༽ U+0F3D 内嵌花括号 ༺༻（2.C1 模板）",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-triangle", Name = "三角翅膀 ꧁༺△", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("꧁༺△", "△༻꧂")],
            Note = "白上三角 △ U+25B3 点缀（2.C1 模板）",
        });
        list.Add(new StyleDefinition
        {
            Id = "wing-love", Name = "爱心边框 •°•❤", Category = TextStyleCategory.Decoration,
            Steps = [new WrapStringStep("•°•❤•°•", "•°•❤•°•")],
            Note = "• U+2022 ° U+00B0 ❤ U+2764 组成的 Love 边框（2.C3 模板）",
        });

        // ================= 数字专用变体（调研报告 2.A6，仅数字表，其余回退原字） =================

        list.Add(new StyleDefinition
        {
            Id = "digits-circled-sans", Name = "无衬线圈数字 ➀", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("digits-circled-sans")],
            Note = "1–9 → ➀–➈（Dingbat 无衬线圈数字 U+2780–2788；区段共 1–10 十个字形、无 0，0 与其它字符回退原字）",
        });
        list.Add(new StyleDefinition
        {
            Id = "digits-double-circled", Name = "双圈数字 ⓵", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("digits-double-circled")],
            Note = "1–9 → ⓵–⓽（双圈数字 U+24F5–24FD；区段共 1–10 十个字形、无 0，0 与其它字符回退原字）",
        });

        // ================= 俄化（形近西里尔字母，LatinFancy） =================

        list.Add(new StyleDefinition
        {
            Id = "cyrillic-lookalike", Name = "俄化体（西里尔形近）", Category = TextStyleCategory.LatinFancy,
            Steps = [new UseMapStep("cyrillic-lookalike")],
            Note = "形近西里尔大写替换：A→А B→В E→Е K→К M→М H→Н O→О P→Р C→С T→Т X→Х Y→У（U+0410 区）",
        });

        // ================= 火星文还原（Chinese，反向字典） =================

        list.Add(new StyleDefinition
        {
            Id = "martian-reverse", Name = "火星文还原", Category = TextStyleCategory.Chinese,
            Steps = [new UseMapStep("martian-reverse")],
            Note = "由 MartianDictionary 反向构建（火星文 → 简体；一对多时取第一个，非 BMP 字形跳过）",
        });

        // ================= 中文扩充（简繁 / 拼音，OpenCC + pinyin-data 字典惰性加载） =================

        list.Add(new StyleDefinition
        {
            Id = "simplified-to-traditional", Name = "简体 → 繁体", Category = TextStyleCategory.Chinese,
            Steps = [new AlgorithmStep(KnownAlgorithm.SimplifiedToTraditional)],
            Note = "OpenCC 字典词级最长匹配（头发→頭髮、皇后→皇后），一对多按词消歧；未收录字符原样保留",
        });
        list.Add(new StyleDefinition
        {
            Id = "traditional-to-simplified", Name = "繁体 → 简体", Category = TextStyleCategory.Chinese,
            Steps = [new AlgorithmStep(KnownAlgorithm.TraditionalToSimplified)],
            Note = "OpenCC 字典词级最长匹配（電腦→电脑）；未收录字符原样保留",
        });
        list.Add(new StyleDefinition
        {
            Id = "pinyin", Name = "拼音（带调）nǐ hǎo", Category = TextStyleCategory.Chinese,
            Steps = [new AlgorithmStep(KnownAlgorithm.Pinyin)],
            Note = "汉字 → 带声调拼音（mozillazg/pinyin-data，基本区 2.4 万字），词间空格；多音字取最常用读音，非汉字原样保留",
        });
        list.Add(new StyleDefinition
        {
            Id = "pinyin-abbr", Name = "拼音缩写 nhm", Category = TextStyleCategory.Chinese,
            Steps = [new AlgorithmStep(KnownAlgorithm.PinyinAbbr)],
            Note = "汉字 → 拼音首字母（你好吗→nhm 网络缩写文体）；英文与数字原样保留",
        });

        // ================= 编码（调研报告 2.E） =================

        list.Add(new StyleDefinition
        {
            Id = "braille", Name = "盲文", Category = TextStyleCategory.Encoding,
            Steps = [new AlgorithmStep(KnownAlgorithm.Braille)],
            Note = "grade-1 逐字母：a–z 按 8 点盲文点位映射（a=⠁ U+2801 起），大小写同形；数字串前加数字符 ⠼ U+283C，1–9/0 复用 a–j 形",
        });
        list.Add(new StyleDefinition
        {
            Id = "nato", Name = "NATO 字母表", Category = TextStyleCategory.Encoding,
            Steps = [new AlgorithmStep(KnownAlgorithm.Nato)],
            Note = "字母 → ICAO/NATO 音标单词（Alfa Bravo … Zulu），大小写同形，非字母忽略",
        });
        list.Add(new StyleDefinition
        {
            Id = "a1z26", Name = "A1Z26", Category = TextStyleCategory.Encoding,
            Steps = [new AlgorithmStep(KnownAlgorithm.A1Z26)],
            Note = "a=1 … z=26：字母间用连字符、词间用空格（字母密码）",
        });
        list.Add(new StyleDefinition
        {
            Id = "binary", Name = "二进制", Category = TextStyleCategory.Encoding,
            Steps = [new AlgorithmStep(KnownAlgorithm.Binary)],
            Note = "UTF-8 字节 → 每字节 8 位二进制，空格分隔（ASCII 字符即每字符 8 位）",
        });
        list.Add(new StyleDefinition
        {
            Id = "hex", Name = "十六进制", Category = TextStyleCategory.Encoding,
            Steps = [new AlgorithmStep(KnownAlgorithm.Hex)],
            Note = "UTF-8 字节 → 每字节 2 位大写十六进制，空格分隔（ASCII 字符即每字符 2 位）",
        });

        // ================= 变换补充（评审建议的标配三项） =================

        list.Add(new StyleDefinition
        {
            Id = "uppercase", Name = "全大写 ABC", Category = TextStyleCategory.Transform,
            Steps = [new AlgorithmStep(KnownAlgorithm.Uppercase)],
            Note = "全部字母转大写（不变文化规则）",
        });
        list.Add(new StyleDefinition
        {
            Id = "lowercase", Name = "全小写 abc", Category = TextStyleCategory.Transform,
            Steps = [new AlgorithmStep(KnownAlgorithm.Lowercase)],
            Note = "全部字母转小写（不变文化规则）",
        });
        list.Add(new StyleDefinition
        {
            Id = "alternating-case", Name = "交替大小写 aLt", Category = TextStyleCategory.Transform,
            Steps = [new AlgorithmStep(KnownAlgorithm.AlternatingCase)],
            Note = "大小写逐字交替（sPoNgE cAsE），跳过非字母不计相位",
        });
    }
}
