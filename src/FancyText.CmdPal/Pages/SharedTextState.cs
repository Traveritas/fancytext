namespace FancyText.CmdPal.Pages;

/// <summary>
/// 跨页面共享的"当前文本"。写入方（首页 / Fallback 根搜索框）更新时广播事件，
/// 所有订阅的样式页立即调度重建——保证导航进入二级页时看到的一定是最新文本。
/// </summary>
internal sealed class SharedTextState
{
    private readonly Lock _gate = new();
    private string? _text;

    public string? Text
    {
        get
        {
            lock (_gate)
            {
                return _text;
            }
        }
        set
        {
            lock (_gate)
            {
                if (string.Equals(_text, value, StringComparison.Ordinal))
                {
                    return;
                }

                _text = value;
            }

            TextChanged?.Invoke(value);
        }
    }

    /// <summary>文本被首页解析或 Fallback query 覆盖时触发（在写入方线程上调用）。</summary>
    public event Action<string?>? TextChanged;
}
