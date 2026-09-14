namespace FancyText.Core;

/// <summary>
/// 声明式步骤管道的编译器：把 <see cref="TransformStep"/> 列表编译成单个委托。
/// 编译在样式构建时一次完成，运行时是纯委托链，与手写组合闭包同构（无逐次分发开销）。
/// </summary>
internal static class StyleInterpreter
{
    public static Func<string, string> Compile(IReadOnlyList<TransformStep> steps) =>
        Build(steps, index: 0, head: static s => s);

    /// <summary>从前往后逐层包裹：head 是「第 index 步之前所有步骤的合成」，返回含第 index 步起的完整管道。</summary>
    private static Func<string, string> Build(IReadOnlyList<TransformStep> steps, int index, Func<string, string> head)
    {
        if (index >= steps.Count)
        {
            return head;
        }

        return steps[index] switch
        {
            // 守卫需要同时看到原文与前置结果，只能在管道组装层展开
            IfChangedStep guard => s =>
            {
                var mid = head(s);
                return string.Equals(mid, s, StringComparison.Ordinal)
                    ? s
                    : Build(steps, index + 1, m => ApplyStep(guard.Inner, m))(mid);
            },
            var step => Build(steps, index + 1, s => ApplyStep(step, head(s))),
        };
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
