using System.Text.Json;

namespace FancyText.Core;

/// <summary>
/// 收藏与最近使用状态，持久化到 %LOCALAPPDATA%\FancyText\state.json（CmdPal 插件与桌面版共享）。
/// 状态变化广播 <see cref="Changed"/>，宿主 UI 据此刷新。
/// </summary>
public sealed class UsageState
{
    private const int RecentCap = 8;
    private const int PinnedCap = 20;

    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FancyText", "state.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _gate = new();
    private List<string> _pinned = [];
    private List<string> _recent = [];

    public UsageState()
    {
        Load();
    }

    public event Action? Changed;

    public IReadOnlyList<string> Pinned
    {
        get
        {
            lock (_gate)
            {
                return _pinned.ToArray();
            }
        }
    }

    public IReadOnlyList<string> Recent
    {
        get
        {
            lock (_gate)
            {
                return _recent.ToArray();
            }
        }
    }

    public bool IsPinned(string styleId)
    {
        lock (_gate)
        {
            return _pinned.Contains(styleId);
        }
    }

    public void TogglePin(string styleId)
    {
        lock (_gate)
        {
            if (!_pinned.Remove(styleId))
            {
                _pinned.Insert(0, styleId);
                if (_pinned.Count > PinnedCap)
                {
                    _pinned.RemoveRange(PinnedCap, _pinned.Count - PinnedCap);
                }
            }
        }

        Save();
        Changed?.Invoke();
    }

    public void RecordUse(string styleId)
    {
        lock (_gate)
        {
            if (_recent.Count > 0 && _recent[0] == styleId)
            {
                return; // 连续使用同一样式不重复记录
            }

            _recent.Remove(styleId);
            _recent.Insert(0, styleId);
            if (_recent.Count > RecentCap)
            {
                _recent.RemoveRange(RecentCap, _recent.Count - RecentCap);
            }
        }

        Save();
        Changed?.Invoke();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(StatePath));
            var root = doc.RootElement;
            if (root.TryGetProperty("pinned", out var pinned) && pinned.ValueKind == JsonValueKind.Array)
            {
                _pinned = pinned.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }

            if (root.TryGetProperty("recent", out var recent) && recent.ValueKind == JsonValueKind.Array)
            {
                _recent = recent.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
        }
        catch (Exception)
        {
            // 状态文件损坏时按空状态启动
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            string json;
            lock (_gate)
            {
                json = JsonSerializer.Serialize(new { pinned = _pinned, recent = _recent }, JsonOptions);
            }

            File.WriteAllText(StatePath, json);
        }
        catch (Exception)
        {
            // 持久化失败不影响本次会话
        }
    }
}
