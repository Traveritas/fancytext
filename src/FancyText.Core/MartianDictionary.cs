using System.Reflection;
using System.Text.Json;

namespace FancyText.Core;

/// <summary>
/// 火星文字典（简体 → 火星文异体字）。
/// 数据来自 cnchar（MIT License，https://github.com/theajack/cnchar ）的 spark-simple.json：
/// {"simple": "…", "spark": "…"} 两条等长平行字符串按索引对应。
/// 本地以嵌入资源随程序集分发，首次访问时解析。
/// </summary>
public static class MartianDictionary
{
    private const string ResourceName = "FancyText.Core.Resources.spark-simple.json";

    private static IReadOnlyDictionary<char, string>? _map;

    public static IReadOnlyDictionary<char, string> Map => _map ??= Load();

    public static int EntryCount => Map.Count;

    private static IReadOnlyDictionary<char, string> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"嵌入资源 {ResourceName} 不存在");

        using var doc = JsonDocument.Parse(stream);
        var simple = doc.RootElement.GetProperty("simple").GetString()
            ?? throw new InvalidOperationException("火星文字典缺少 simple 字段");
        var spark = doc.RootElement.GetProperty("spark").GetString()
            ?? throw new InvalidOperationException("火星文字典缺少 spark 字段");

        // 按 Unicode 标量配对（字典中存在增补平面字符，不能按 UTF-16 码元切分）
        var simpleRunes = simple.EnumerateRunes().ToArray();
        var sparkRunes = spark.EnumerateRunes().ToArray();
        if (simpleRunes.Length != sparkRunes.Length)
        {
            throw new InvalidOperationException($"火星文字典长度不一致：simple={simpleRunes.Length}, spark={sparkRunes.Length}");
        }

        var map = new Dictionary<char, string>(simpleRunes.Length);
        for (var i = 0; i < simpleRunes.Length; i++)
        {
            // 键必须能放进 char（BMP）；简体一侧理论上全为常用字，此判断仅防御异常数据
            if (simpleRunes[i].Value > 0xFFFF)
            {
                continue;
            }

            map[(char)simpleRunes[i].Value] = sparkRunes[i].ToString();
        }

        return map;
    }
}
