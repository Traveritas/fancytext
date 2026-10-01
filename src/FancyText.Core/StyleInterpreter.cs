namespace FancyText.Core;

/// <summary>
/// 声明式步骤管道的编译器：把 <see cref="TransformStep"/> 列表在构建期组装成一条委托链。
/// 运行时每步仍按步骤类型分派一次（开销可忽略）；守卫步骤在每次调用时展开其后续链。
/// </summary>
internal static class StyleInterpreter
{
    /// <summary>
    /// 编译管道。<paramref name="maxStepOutputChars"/> 非空时（外部包样式）每步输出超限即截断，并吞掉异常回退原文，
    /// 兑现 <see cref="TextStyle.Transform"/>「不得抛异常」的契约；内置样式不加这层。
    /// </summary>
    public static Func<string, string> Compile(IReadOnlyList<TransformStep> steps, int? maxStepOutputChars = null)
    {
        var pipeline = Build(steps, index: 0, head: static s => s, maxStepOutputChars);
        if (maxStepOutputChars is null)
        {
            return s => pipeline(s ?? string.Empty);
        }

        return s =>
        {
            s ??= string.Empty;
            try
            {
                return pipeline(s);
            }
            catch (Exception)
            {
                return s;
            }
        };
    }

    /// <summary>从前往后逐层包裹：head 是「第 index 步之前所有步骤的合成」，返回含第 index 步起的完整管道。</summary>
    private static Func<string, string> Build(IReadOnlyList<TransformStep> steps, int index, Func<string, string> head, int? cap)
    {
        if (index >= steps.Count)
        {
            return head;
        }

        return steps[index] switch
        {
            // 守卫需要同时看到原文与前置结果，只能在管道组装层展开（比较的是此前全部步骤的输出与原文）
            IfChangedStep guard => BuildGuard(steps, index, guard, head, cap),
            var step => Build(steps, index + 1, s => Cap(ApplyStep(step, head(s)), cap), cap),
        };
    }

    /// <summary>
    /// 守卫：前置步骤没有作用于输入时短路返回原文。紧跟在首步映射之后时按「输入里有没有映射表覆盖的字符」判断——
    /// 表里有恒等项（倒转表 o→o、l→l、s→s），只比较输出是否变化会把 solo 这类词误判为不适用。
    /// </summary>
    private static Func<string, string> BuildGuard(
        IReadOnlyList<TransformStep> steps, int index, IfChangedStep guard, Func<string, string> head, int? cap)
    {
        var leadingMap = index == 1
            ? steps[0] switch
            {
                MapReplaceStep map => map.Map,
                UseMapStep useMap => KnownTransforms.ResolveMap(useMap.MapName),
                _ => null,
            }
            : null;
        var rest = Build(steps, index + 1, m => Cap(ApplyStep(guard.Inner, m), cap), cap);
        return s =>
        {
            var mid = head(s);
            var applicable = !string.Equals(mid, s, StringComparison.Ordinal)
                || (leadingMap is not null && ContainsAnyKey(s, leadingMap));
            return applicable ? rest(mid) : s;
        };
    }

    private static bool ContainsAnyKey(string text, IReadOnlyDictionary<char, string> map)
    {
        foreach (var c in text)
        {
            if (map.ContainsKey(c))
            {
                return true;
            }
        }

        return false;
    }

    private static string Cap(string text, int? cap)
    {
        if (cap is not { } max || text.Length <= max)
        {
            return text;
        }

        var cut = char.IsHighSurrogate(text[max - 1]) ? max - 1 : max; // 不拆代理对
        return text[..cut];
    }

    private static string ApplyStep(TransformStep step, string input) => step switch
    {
        MapReplaceStep map => TextTransforms.MapReplace(input, map.Map),
        UseMapStep useMap => TextTransforms.MapReplace(input, KnownTransforms.ResolveMap(useMap.MapName)),
        AppendMarkStep mark => TextTransforms.AppendMark(input, mark.Mark, mark.Repeat),
        WrapStringStep wrap => TextTransforms.WrapString(input, wrap.Prefix, wrap.Suffix),
        WrapEachStep wrapEach => TextTransforms.WrapEach(input, wrapEach.Prefix, wrapEach.Suffix),
        SpacingStep spacing => TextTransforms.Spacing(input, spacing.Separator),
        ReverseStep => TextTransforms.Reverse(input),
        AlgorithmStep algorithm => KnownTransforms.ApplyAlgorithm(algorithm.Algorithm, input),
        _ => input,
    };
}
