namespace FancyText.Core;

/// <summary>样式来源：内置（随引擎编译）或外部样式包导入。</summary>
public abstract record StyleSource
{
    /// <summary>内置样式（StyleCatalog 编译期定义）。</summary>
    public sealed record BuiltIn() : StyleSource;

    /// <summary>外部样式包导入的样式。</summary>
    public sealed record Pack(string PackName) : StyleSource;
}

/// <summary>样式的可序列化定义：元数据 + 声明式步骤管道（<see cref="StyleInterpreter"/> 编译执行）。</summary>
public sealed record StyleDefinition
{
    /// <summary>稳定 ID（kebab-case）。包内样式建议加包名前缀，避免与内置或其他包撞车。</summary>
    public required string Id { get; init; }

    /// <summary>显示名（中文优先，可带英文别名）。</summary>
    public required string Name { get; init; }

    public required TextStyleCategory Category { get; init; }

    /// <summary>机制说明（码点/来源），显示在详情页。</summary>
    public string? Note { get; init; }

    /// <summary>转换管道，按序应用；不得为空。</summary>
    public required IReadOnlyList<TransformStep> Steps { get; init; }
}

/// <summary>由 <see cref="StyleDefinition"/> 编译出可执行的 <see cref="TextStyle"/>（内置与外部包的唯一正式入口）。</summary>
public static class StyleFactory
{
    public static TextStyle FromDefinition(StyleDefinition definition, StyleSource? source = null)
    {
        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            throw new ArgumentException("样式 Id 不能为空", nameof(definition));
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new ArgumentException($"样式 {definition.Id} 缺少名称", nameof(definition));
        }

        if (definition.Steps is not { Count: > 0 })
        {
            throw new ArgumentException($"样式 {definition.Id} 至少需要一个转换步骤", nameof(definition));
        }

        return new TextStyle
        {
            Id = definition.Id,
            Name = definition.Name,
            Category = definition.Category,
            Note = definition.Note,
            Definition = definition,
            Source = source ?? new StyleSource.BuiltIn(),
            Transform = StyleInterpreter.Compile(definition.Steps),
        };
    }
}
