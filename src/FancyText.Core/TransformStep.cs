namespace FancyText.Core;

/// <summary>
/// 声明式转换步骤：一个样式 = 若干步骤按序组成的纯数据管道。
/// 与 <see cref="TextStyle.Transform"/> 的编译期委托等价，但可序列化——内置样式与外部样式包共用同一表达。
/// 步骤集合刻意保持正交精简：查表、附加组合符、包围、分隔、倒序、内置算法、守卫。
/// </summary>
public abstract record TransformStep;

/// <summary>逐字符查表替换（值为 string 以支持增补平面字符，如 𝐀 🅐）。外部包用内联小表。</summary>
public sealed record MapReplaceStep(IReadOnlyDictionary<char, string> Map) : TransformStep;

/// <summary>按名称引用引擎内置映射表（bold / martian 等，见 KnownTransforms）。内置样式借此共享程序化生成的表。</summary>
public sealed record UseMapStep(string MapName) : TransformStep;

/// <summary>每个字素后追加组合附加符号串；<paramref name="Repeat"/> 用于堆叠出冒烟等效果。</summary>
public sealed record AppendMarkStep(string Mark, int Repeat = 1) : TransformStep;

/// <summary>整串前后包围（翅膀、边框、括号模板）。</summary>
public sealed record WrapStringStep(string Prefix, string Suffix) : TransformStep;

/// <summary>逐字素前后包围。</summary>
public sealed record WrapEachStep(string Prefix, string Suffix) : TransformStep;

/// <summary>字素间插入分隔符（aesthetic 分字、宽体）。</summary>
public sealed record SpacingStep(string Separator) : TransformStep;

/// <summary>按字素（而非 UTF-16 码元）倒序，不拆散代理对与组合符序列。</summary>
public sealed record ReverseStep() : TransformStep;

/// <summary>调用引擎内置算法（编码 / Zalgo / 大小写等，见 KnownTransforms）。</summary>
public sealed record AlgorithmStep(KnownAlgorithm Algorithm) : TransformStep;

/// <summary>
/// 守卫步骤：当前中间结果与原文相同（前置映射零命中）时短路，最终输出原文；
/// 否则应用 <paramref name="Inner"/> 后继续后续步骤。
/// 用于「仅对可映射输入生效」的语义（宽体、倒转/镜像文字：纯中文输入时视为不适用，由 UI 过滤）。
/// </summary>
public sealed record IfChangedStep(TransformStep Inner) : TransformStep;

/// <summary>引擎内置算法名。外部包可引用但不能扩展（扩展请用 MapReplaceStep 表达）。</summary>
public enum KnownAlgorithm
{
    ZalgoMini,
    ZalgoNormal,
    ZalgoMax,
    Base64,
    Rot13,
    Morse,
    Braille,
    Nato,
    A1Z26,
    Binary,
    Hex,
    Uppercase,
    Lowercase,
    AlternatingCase,
    StripCombiningMarks,
    SimplifiedToTraditional,
    TraditionalToSimplified,
    Pinyin,
    PinyinAbbr,
}
