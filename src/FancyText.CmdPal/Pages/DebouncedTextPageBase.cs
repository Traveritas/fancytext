using System.Globalization;
using FancyText.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace FancyText.CmdPal.Pages;

/// <summary>
/// 动态文本页基类：防抖重建、文本去重、剪贴板 3 秒缓存、按字素截断、诊断日志、持久项刷新钩子。
/// 性能契约见 <see cref="FancyTextStylesPage"/>：分配与「刷新次数 × 文本长度」解耦。
/// </summary>
internal abstract partial class DebouncedTextPageBase : DynamicListPage, IDisposable
{
    protected const int DebounceMs = 250;
    private static readonly TimeSpan ClipboardCacheInterval = TimeSpan.FromSeconds(3);

    protected static readonly string DiagLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FancyText", "diag.log");

    private readonly Lock _gate = new();
    private readonly string _diagTag;
    private readonly bool _readSharedText;

    private IListItem[] _currentItems = [];
    private string _lastBuiltText = "\u0000initial"; // 与任何合法输入不同，强制首次构建
    private Timer? _rebuildTimer;
    private string? _clipboardCache;
    private DateTime _clipboardReadAtUtc = DateTime.MinValue;
    private int _rebuildCount;
    private bool _disposed;

    protected DebouncedTextPageBase(SharedTextState shared, string diagTag, bool readSharedText)
    {
        Shared = shared;
        _diagTag = diagTag;
        _readSharedText = readSharedText;
        if (readSharedText)
        {
            Shared.TextChanged += OnSharedTextChanged;
        }
    }

    private void OnSharedTextChanged(string? text) => ScheduleRebuild(delayMs: 0);

    protected SharedTextState Shared { get; }

    /// <summary>fallback 直传预留（CmdPal 根搜索框的 query，待适配 FallbackHandler 后写入）。</summary>
    protected string? SeedText { get; set; }

    /// <summary>子类实现：对给定文本刷新持久列表项并返回过滤后的数组。不得抛异常。</summary>
    protected abstract IListItem[] BuildItems(string text);

    /// <summary>文本解析完成后回调（首页用它写入 <see cref="Shared"/>）。</summary>
    protected virtual void OnTextResolved(string text)
    {
    }

    /// <summary>在派生类构造函数末尾调用：同步构建初始项并调度一次立即重建。</summary>
    protected void Initialize()
    {
        var text = ResolveText(useSharedText: _readSharedText, readClipboard: false);
        _currentItems = BuildItems(text);
        _lastBuiltText = text;
        OnTextResolved(text);
        ScheduleRebuild(delayMs: 0); // 打开即可交互，随后异步换成剪贴板内容
    }

    public override void UpdateSearchText(string oldSearch, string newSearch) => ScheduleRebuild();

    public override IListItem[] GetItems()
    {
        lock (_gate)
        {
            return _currentItems.ToArray();
        }
    }

    protected void ScheduleRebuild(int delayMs = DebounceMs)
    {
        if (_disposed)
        {
            return;
        }

        var timer = new Timer(_ =>
        {
            try
            {
                Rebuild();
            }
            catch (Exception)
            {
                // 重建失败保留旧列表，不打断宿主
            }
        }, null, delayMs, Timeout.Infinite);

        var old = Interlocked.Exchange(ref _rebuildTimer, timer);
        old?.Dispose();
    }

    private void Rebuild()
    {
        if (_disposed)
        {
            return;
        }

        var text = ResolveText(useSharedText: _readSharedText, readClipboard: true);

        lock (_gate)
        {
            if (string.Equals(text, _lastBuiltText, StringComparison.Ordinal))
            {
                return;
            }
        }

        var items = BuildItems(text);
        OnTextResolved(text);

        lock (_gate)
        {
            _currentItems = items;
            _lastBuiltText = text;
        }

        RaiseItemsChanged();
        WriteDiag(text, items.Length);
    }

    /// <summary>文本优先级：搜索框输入 &gt; SeedText &gt; 首页共享文本（仅样式页）&gt; 剪贴板（缓存）&gt; 内置示例。</summary>
    private string ResolveText(bool useSharedText, bool readClipboard)
    {
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            return SearchText!;
        }

        if (!string.IsNullOrWhiteSpace(SeedText))
        {
            return SeedText!;
        }

        if (useSharedText && !string.IsNullOrWhiteSpace(Shared.Text))
        {
            return Shared.Text!;
        }

        if (readClipboard)
        {
            if (DateTime.UtcNow - _clipboardReadAtUtc > ClipboardCacheInterval)
            {
                _clipboardCache = Helpers.ClipboardBridge.TryGetText();
                _clipboardReadAtUtc = DateTime.UtcNow;
            }

            if (!string.IsNullOrWhiteSpace(_clipboardCache))
            {
                return _clipboardCache!;
            }
        }

        return StyleCatalog.DefaultSample;
    }

    /// <summary>按字素截断。增量扫描、凑满即停——代价只与结果长度成正比，与输入全文长度无关。</summary>
    protected static string TruncateTextElements(string text, int maxElements)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxElements)
        {
            return text; // UTF-16 长度不超限时字素数必然不超限
        }

        var elements = 0;
        var i = 0;
        while (i < text.Length)
        {
            // 基本字符：代理对按一个字素处理
            if (char.IsHighSurrogate(text[i]))
            {
                i += i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
            }
            else
            {
                i++;
            }

            // 粘住后续组合记号（组成一个完整字素）
            while (i < text.Length && IsCombining(text[i]))
            {
                i++;
            }

            elements++;
            if (elements == maxElements)
            {
                return i >= text.Length ? text : text[..i];
            }
        }

        return text;
    }

    private static bool IsCombining(char c)
    {
        var category = char.GetUnicodeCategory(c);
        return category == UnicodeCategory.NonSpacingMark
            || category == UnicodeCategory.SpacingCombiningMark
            || category == UnicodeCategory.EnclosingMark;
    }

    private void WriteDiag(string text, int itemCount)
    {
        try
        {
            var count = ++_rebuildCount;
            if (count % 100 == 0 && File.Exists(DiagLogPath) && new FileInfo(DiagLogPath).Length > 1_000_000)
            {
                File.Delete(DiagLogPath); // 防日志无限增长
            }

            Directory.CreateDirectory(Path.GetDirectoryName(DiagLogPath)!);
            var line =
                $"{DateTime.Now:HH:mm:ss.fff} page={_diagTag} rebuild={count} textLen={text.Length} items={itemCount} " +
                $"managed={GC.GetTotalMemory(false) / 1024}KB ws={Environment.WorkingSet / 1024}KB " +
                $"gc0={GC.CollectionCount(0)} gc1={GC.CollectionCount(1)} gc2={GC.CollectionCount(2)}";
            File.AppendAllText(DiagLogPath, line + Environment.NewLine);
        }
        catch (Exception)
        {
            // 诊断日志失败不影响功能
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_readSharedText)
        {
            Shared.TextChanged -= OnSharedTextChanged;
        }

        _rebuildTimer?.Dispose();
        _rebuildTimer = null;
        OnDisposing();
    }

    /// <summary>派生类在此退订自己的事件（UsageState.Changed 等）。</summary>
    protected virtual void OnDisposing()
    {
    }
}
