using System.Text.Json;
using System.Text.Json.Serialization;

namespace FancyText.Core;

/// <summary>样式包 JSON 的命名约定：分类/算法的 kebab-case 映射 + 包级序列化选项。</summary>
internal static class StylePackJson
{
    public static readonly JsonSerializerOptions PackOptions = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            // 包是给人分享/手改的：中文与花式字符原样输出（与 CLI --json 一致），null 字段省略
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new StyleDefinitionJsonConverter());
        options.Converters.Add(new TransformStepJsonConverter());
        return options;
    }

    public static string ToKebab(TextStyleCategory category) => category switch
    {
        TextStyleCategory.CjkEffect => "effect",
        TextStyleCategory.LatinFancy => "latin-fancy",
        TextStyleCategory.Decoration => "decoration",
        TextStyleCategory.Transform => "transform",
        TextStyleCategory.Chinese => "chinese",
        TextStyleCategory.Encoding => "encoding",
        _ => category.ToString(),
    };

    public static bool TryParseCategory(string name, out TextStyleCategory category)
    {
        switch (name)
        {
            case "effect":
            case "cjk-effect": // 兼容历史写法（分类曾叫「中文特效」）
                category = TextStyleCategory.CjkEffect; return true;
            case "latin-fancy": category = TextStyleCategory.LatinFancy; return true;
            case "decoration": category = TextStyleCategory.Decoration; return true;
            case "transform": category = TextStyleCategory.Transform; return true;
            case "chinese": category = TextStyleCategory.Chinese; return true;
            case "encoding": category = TextStyleCategory.Encoding; return true;
            default: category = default; return false;
        }
    }

    public static string ToKebab(KnownAlgorithm algorithm) => algorithm switch
    {
        KnownAlgorithm.ZalgoMini => "zalgo-mini",
        KnownAlgorithm.ZalgoNormal => "zalgo-normal",
        KnownAlgorithm.ZalgoMax => "zalgo-max",
        KnownAlgorithm.Base64 => "base64",
        KnownAlgorithm.Rot13 => "rot13",
        KnownAlgorithm.Morse => "morse",
        KnownAlgorithm.Braille => "braille",
        KnownAlgorithm.Nato => "nato",
        KnownAlgorithm.A1Z26 => "a1z26",
        KnownAlgorithm.Binary => "binary",
        KnownAlgorithm.Hex => "hex",
        KnownAlgorithm.Uppercase => "uppercase",
        KnownAlgorithm.Lowercase => "lowercase",
        KnownAlgorithm.AlternatingCase => "alternating-case",
        KnownAlgorithm.StripCombiningMarks => "strip-combining-marks",
        KnownAlgorithm.SimplifiedToTraditional => "simplified-to-traditional",
        KnownAlgorithm.TraditionalToSimplified => "traditional-to-simplified",
        KnownAlgorithm.Pinyin => "pinyin",
        KnownAlgorithm.PinyinAbbr => "pinyin-abbr",
        _ => algorithm.ToString(),
    };

    public static bool TryParseAlgorithm(string name, out KnownAlgorithm algorithm)
    {
        switch (name)
        {
            case "zalgo-mini": algorithm = KnownAlgorithm.ZalgoMini; return true;
            case "zalgo-normal": algorithm = KnownAlgorithm.ZalgoNormal; return true;
            case "zalgo-max": algorithm = KnownAlgorithm.ZalgoMax; return true;
            case "base64": algorithm = KnownAlgorithm.Base64; return true;
            case "rot13": algorithm = KnownAlgorithm.Rot13; return true;
            case "morse": algorithm = KnownAlgorithm.Morse; return true;
            case "braille": algorithm = KnownAlgorithm.Braille; return true;
            case "nato": algorithm = KnownAlgorithm.Nato; return true;
            case "a1z26": algorithm = KnownAlgorithm.A1Z26; return true;
            case "binary": algorithm = KnownAlgorithm.Binary; return true;
            case "hex": algorithm = KnownAlgorithm.Hex; return true;
            case "uppercase": algorithm = KnownAlgorithm.Uppercase; return true;
            case "lowercase": algorithm = KnownAlgorithm.Lowercase; return true;
            case "alternating-case": algorithm = KnownAlgorithm.AlternatingCase; return true;
            case "strip-combining-marks": algorithm = KnownAlgorithm.StripCombiningMarks; return true;
            case "simplified-to-traditional": algorithm = KnownAlgorithm.SimplifiedToTraditional; return true;
            case "traditional-to-simplified": algorithm = KnownAlgorithm.TraditionalToSimplified; return true;
            case "pinyin": algorithm = KnownAlgorithm.Pinyin; return true;
            case "pinyin-abbr": algorithm = KnownAlgorithm.PinyinAbbr; return true;
            default: algorithm = default; return false;
        }
    }
}

/// <summary>
/// 样式定义序列化：category 字段写 6 个内置 kebab 名之一走内置类，
/// 其余任意非空字符串（≤ <see cref="StylePacks.MaxCategoryNameLength"/> 码元、健康字符）视为包自定义类别，
/// 原文存入 <see cref="StyleDefinition.CustomCategoryName"/> 并在界面原样显示。
/// </summary>
internal sealed class StyleDefinitionJsonConverter : JsonConverter<StyleDefinition>
{
    public override StyleDefinition? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var element = doc.RootElement;
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("样式必须是 JSON 对象");
        }

        string? id = null, name = null, customCategory = null, note = null, noteEn = null, nameEn = null;
        var category = default(TextStyleCategory);
        var categorySet = false;
        IReadOnlyList<TransformStep>? steps = null;
        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "id":
                    id = property.Value.GetString();
                    break;
                case "name":
                    name = property.Value.GetString();
                    break;
                case "nameEn":
                    nameEn = property.Value.GetString();
                    break;
                case "category":
                    var categoryName = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                    if (string.IsNullOrWhiteSpace(categoryName))
                    {
                        throw new JsonException("category 不能为空");
                    }

                    if (StylePacks.IsHealthyText(categoryName, StylePacks.MaxCategoryNameLength))
                    {
                        if (StylePackJson.TryParseCategory(categoryName, out category))
                        {
                            customCategory = null;
                        }
                        else
                        {
                            category = TextStyleCategory.Custom;
                            customCategory = categoryName;
                        }

                        categorySet = true;
                    }
                    else
                    {
                        throw new JsonException(
                            $"自定义类别 \"{categoryName}\" 不合法（{StylePacks.MaxCategoryNameLength} 码元以内，禁止孤立代理对与控制字符；内置类别可用 effect / latin-fancy / decoration / transform / chinese / encoding）");
                    }

                    break;
                case "note":
                    note = property.Value.GetString();
                    break;
                case "noteEn":
                    noteEn = property.Value.GetString();
                    break;
                case "steps":
                    steps = property.Value.Deserialize<IReadOnlyList<TransformStep>>(options);
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new JsonException("样式缺少 id");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new JsonException($"样式 {id} 缺少 name");
        }

        if (!categorySet)
        {
            throw new JsonException($"样式 {id} 缺少 category");
        }

        if (steps is not { Count: > 0 })
        {
            throw new JsonException($"样式 {id} 缺少 steps");
        }

        return new StyleDefinition
        {
            Id = id,
            Name = name,
            NameEn = nameEn,
            Category = category,
            CustomCategoryName = customCategory,
            Note = note,
            NoteEn = noteEn,
            Steps = steps,
        };
    }

    public override void Write(Utf8JsonWriter writer, StyleDefinition value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        writer.WriteString("name", value.Name);
        if (value.NameEn is not null)
        {
            writer.WriteString("nameEn", value.NameEn);
        }

        writer.WriteString(
            "category",
            value.Category == TextStyleCategory.Custom ? value.CustomCategoryName : StylePackJson.ToKebab(value.Category));
        if (value.Note is not null)
        {
            writer.WriteString("note", value.Note);
        }

        if (value.NoteEn is not null)
        {
            writer.WriteString("noteEn", value.NoteEn);
        }

        writer.WritePropertyName("steps");
        JsonSerializer.Serialize(writer, value.Steps, options);
        writer.WriteEndObject();
    }
}

/// <summary>
/// 步骤多态序列化：{"op": "mapReplace", ...}。判别字段 op + 每类步骤的字段见各 case；
/// 映射键限单字符（BMP 码元），语义校验（码点健康度/上限/白名单）在 <see cref="StylePacks.Validate"/>。
/// </summary>
internal sealed class TransformStepJsonConverter : JsonConverter<TransformStep>
{
    public override TransformStep? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return ReadElement(doc.RootElement, options);
    }

    private static TransformStep ReadElement(JsonElement element, JsonSerializerOptions options)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("步骤必须是 JSON 对象");
        }

        if (!element.TryGetProperty("op", out var opProperty)
            || opProperty.ValueKind != JsonValueKind.String
            || opProperty.GetString() is not { } op)
        {
            throw new JsonException("步骤缺少 op 判别字段（字符串）");
        }

        switch (op)
        {
            case "mapReplace":
            {
                Require(element, "map", out var mapProperty);
                if (mapProperty.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException("mapReplace 的 map 必须是对象");
                }

                var map = new Dictionary<char, string>();
                foreach (var entry in mapProperty.EnumerateObject())
                {
                    if (entry.Name.Length != 1)
                    {
                        throw new JsonException($"映射键必须是单个字符（BMP），实际 \"{entry.Name}\"");
                    }

                    if (entry.Value.ValueKind != JsonValueKind.String || entry.Value.GetString() is not { } value)
                    {
                        throw new JsonException($"映射 '{entry.Name}' 的值必须是字符串");
                    }

                    map[entry.Name[0]] = value;
                }

                return new MapReplaceStep(map);
            }

            case "useMap":
            {
                Require(element, "map", out var mapProperty);
                return new UseMapStep(RequireString(mapProperty, "useMap 的 map"));
            }

            case "appendMark":
            {
                Require(element, "mark", out var markProperty);
                var repeat = 1;
                if (element.TryGetProperty("repeat", out var repeatProperty))
                {
                    if (repeatProperty.ValueKind != JsonValueKind.Number || !repeatProperty.TryGetInt32(out repeat))
                    {
                        throw new JsonException("appendMark 的 repeat 必须是整数");
                    }
                }

                return new AppendMarkStep(RequireString(markProperty, "appendMark 的 mark"), repeat);
            }

            case "wrapString":
                Require(element, "prefix", out var prefix);
                Require(element, "suffix", out var suffix);
                return new WrapStringStep(RequireString(prefix, "prefix"), RequireString(suffix, "suffix"));

            case "wrapEach":
                Require(element, "prefix", out var eachPrefix);
                Require(element, "suffix", out var eachSuffix);
                return new WrapEachStep(RequireString(eachPrefix, "prefix"), RequireString(eachSuffix, "suffix"));

            case "spacing":
                Require(element, "separator", out var separator);
                return new SpacingStep(RequireString(separator, "separator"));

            case "reverse":
                return new ReverseStep();

            case "algorithm":
            {
                Require(element, "name", out var nameProperty);
                var name = RequireString(nameProperty, "algorithm 的 name");
                if (!StylePackJson.TryParseAlgorithm(name, out var algorithm))
                {
                    throw new JsonException($"未知算法：\"{name}\"");
                }

                return new AlgorithmStep(algorithm);
            }

            case "ifChanged":
            {
                Require(element, "inner", out var innerProperty);
                var inner = innerProperty.ValueKind == JsonValueKind.Object
                    ? ReadElement(innerProperty, options)
                    : throw new JsonException("ifChanged 的 inner 必须是步骤对象");
                return new IfChangedStep(inner);
            }

            default:
                throw new JsonException($"未知步骤类型 op=\"{op}\"");
        }
    }

    public override void Write(Utf8JsonWriter writer, TransformStep value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case MapReplaceStep map:
                writer.WriteString("op", "mapReplace");
                writer.WriteStartObject("map");
                foreach (var (key, replacement) in map.Map)
                {
                    writer.WriteString(key.ToString(), replacement);
                }

                writer.WriteEndObject();
                break;

            case UseMapStep useMap:
                writer.WriteString("op", "useMap");
                writer.WriteString("map", useMap.MapName);
                break;

            case AppendMarkStep mark:
                writer.WriteString("op", "appendMark");
                writer.WriteString("mark", mark.Mark);
                if (mark.Repeat != 1)
                {
                    writer.WriteNumber("repeat", mark.Repeat);
                }

                break;

            case WrapStringStep wrap:
                writer.WriteString("op", "wrapString");
                writer.WriteString("prefix", wrap.Prefix);
                writer.WriteString("suffix", wrap.Suffix);
                break;

            case WrapEachStep wrapEach:
                writer.WriteString("op", "wrapEach");
                writer.WriteString("prefix", wrapEach.Prefix);
                writer.WriteString("suffix", wrapEach.Suffix);
                break;

            case SpacingStep spacing:
                writer.WriteString("op", "spacing");
                writer.WriteString("separator", spacing.Separator);
                break;

            case ReverseStep:
                writer.WriteString("op", "reverse");
                break;

            case AlgorithmStep algorithm:
                writer.WriteString("op", "algorithm");
                writer.WriteString("name", StylePackJson.ToKebab(algorithm.Algorithm));
                break;

            case IfChangedStep guard:
                writer.WriteString("op", "ifChanged");
                writer.WritePropertyName("inner");
                Write(writer, guard.Inner, options);
                break;

            default:
                throw new JsonException($"不支持的步骤类型：{value.GetType().Name}");
        }

        writer.WriteEndObject();
    }

    private static void Require(JsonElement element, string name, out JsonElement property)
    {
        if (!element.TryGetProperty(name, out property))
        {
            throw new JsonException($"缺少字段：{name}");
        }
    }

    private static string RequireString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.String && element.GetString() is { } value
            ? value
            : throw new JsonException($"{name} 必须是字符串");
}
