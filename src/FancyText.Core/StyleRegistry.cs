using System.Net.Http;
using System.Text.Json;

namespace FancyText.Core;

/// <summary>在线样式包索引条目（fancytext-styles 仓库 index.json 的 packs 数组元素）。</summary>
public sealed record RegistryPackEntry(
    string Id,
    string PackName,
    string Description,
    string? Author,
    bool Official,
    string DownloadUrl,
    string? MirrorUrl);

/// <summary>在线获取结果：Value 为 null 时 Error 说明原因（网络失败/格式错误）。</summary>
public sealed record RegistryResult<T>(T? Value, string? Error)
{
    public static RegistryResult<T> Ok(T value) => new(value, null);
    public static RegistryResult<T> Fail(string error) => new(default, error);
}

/// <summary>
/// 样式包在线索引客户端：拉取 fancytext-styles 仓库的 index.json 与包文件。
/// 主通道 raw.githubusercontent.com，失败回退 jsDelivr CDN 镜像（应对直连不稳的网络）；
/// 仅 HTTPS、只读、不做任何写操作——下载的包 JSON 经 <see cref="StylePacks.ImportJson"/>
/// 走与本地导入完全相同的校验管线后落盘。
/// </summary>
public static class StyleRegistry
{
    public const int CurrentIndexSchemaVersion = 1;

    private const string IndexPath = "Traveritas/fancytext-styles/main/index.json";

    public static string IndexUrl { get; } = $"https://raw.githubusercontent.com/{IndexPath}";

    public static string IndexMirrorUrl { get; } = $"https://cdn.jsdelivr.net/gh/{IndexPath}";

    private static readonly Lazy<HttpClient> Client = new(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FancyText-Desktop");
        return client;
    });

    /// <summary>拉取并解析索引（主通道失败回退镜像）；单条缺必要字段的条目跳过不拖累整表。</summary>
    public static async Task<RegistryResult<IReadOnlyList<RegistryPackEntry>>> FetchIndexAsync(CancellationToken ct = default)
    {
        var json = await DownloadTextAsync(
            [
                (IndexUrl, "GitHub raw"),
                (IndexMirrorUrl, "jsDelivr 镜像"),
            ],
            ct).ConfigureAwait(false);
        if (json.Value is null)
        {
            return RegistryResult<IReadOnlyList<RegistryPackEntry>>.Fail(json.Error!);
        }

        return ParseIndex(json.Value, out var packs) is { } error
            ? RegistryResult<IReadOnlyList<RegistryPackEntry>>.Fail(error)
            : RegistryResult<IReadOnlyList<RegistryPackEntry>>.Ok(packs!);
    }

    /// <summary>下载一个包的 JSON 原文（条目自带镜像则失败回退）；长度超过 <see cref="StylePacks.MaxFileBytes"/> 拒收。</summary>
    public static async Task<RegistryResult<string>> DownloadPackAsync(RegistryPackEntry entry, CancellationToken ct = default)
    {
        var channels = new List<(string Url, string Name)> { (entry.DownloadUrl, "GitHub raw") };
        if (!string.IsNullOrWhiteSpace(entry.MirrorUrl))
        {
            channels.Add((entry.MirrorUrl!, "jsDelivr 镜像"));
        }

        var json = await DownloadTextAsync(channels, ct).ConfigureAwait(false);
        if (json.Value is null)
        {
            return RegistryResult<string>.Fail(json.Error!);
        }

        if (json.Value.Length > StylePacks.MaxFileBytes)
        {
            return RegistryResult<string>.Fail($"包「{entry.PackName}」超过 {StylePacks.MaxFileBytes / 1024} KB 上限，已拒收");
        }

        return RegistryResult<string>.Ok(json.Value);
    }

    /// <summary>解析索引 JSON；返回 null 表示成功（packs 为解析结果，空表合法）。</summary>
    internal static string? ParseIndex(string json, out IReadOnlyList<RegistryPackEntry>? packs)
    {
        packs = null;
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return $"索引不是合法 JSON（{ex.Message}）";
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("packs", out var packsEl)
                || packsEl.ValueKind != JsonValueKind.Array)
            {
                return "索引缺少 packs 数组";
            }

            if (root.TryGetProperty("schemaVersion", out var versionEl)
                && (versionEl.ValueKind != JsonValueKind.Number || versionEl.GetInt32() != CurrentIndexSchemaVersion))
            {
                return $"索引 schemaVersion 不受支持（当前 {CurrentIndexSchemaVersion}），请升级 FancyText";
            }

            var result = new List<RegistryPackEntry>();
            foreach (var item in packsEl.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                // 必要字段缺失（id/packName/downloadUrl）跳过该条，不拖累整表
                if (!TryGetString(item, "packName", out var packName)
                    || !TryGetString(item, "downloadUrl", out var downloadUrl)
                    || !TryGetString(item, "id", out var id))
                {
                    continue;
                }

                if (!downloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // 只接受 HTTPS 直链
                }

                TryGetString(item, "mirrorUrl", out var mirrorUrl);
                TryGetString(item, "description", out var description);
                TryGetString(item, "author", out var author);
                item.TryGetProperty("official", out var officialEl);
                result.Add(new RegistryPackEntry(
                    id, packName, description ?? string.Empty, author,
                    officialEl.ValueKind == JsonValueKind.True, downloadUrl, mirrorUrl));
            }

            packs = result;
            return null;
        }
    }

    private static async Task<RegistryResult<string>> DownloadTextAsync(
        IReadOnlyList<(string Url, string Channel)> channels, CancellationToken ct)
    {
        var errors = new List<string>();
        foreach (var (url, channel) in channels)
        {
            try
            {
                var text = await Client.Value.GetStringAsync(url, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return RegistryResult<string>.Ok(text);
                }

                errors.Add($"{channel}: 空响应");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                errors.Add($"{channel}: {ex.Message}");
            }
        }

        return RegistryResult<string>.Fail($"网络获取失败（{string.Join("；", errors)}）");
    }

    private static bool TryGetString(JsonElement parent, string name, out string? value)
    {
        value = null;
        return parent.TryGetProperty(name, out var el)
            && el.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(el.GetString())
            && (value = el.GetString()) is not null;
    }
}
