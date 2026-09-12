using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FancyText.Core;
using FancyText.Desktop.Helpers;
using FancyText.Desktop.Models;

using WinForms = System.Windows.Forms;

namespace FancyText.Desktop;

/// <summary>
/// 弹窗式转换主窗口：常驻隐藏，热键/托盘/二次启动唤出，失焦自动隐藏（此类唤出式工具的标准行为）。
/// 性能约定与 CmdPal 插件一致：预览只转换前 64 个字素（输入防抖 150ms 后整体重建列表），
/// 回车复制时才对全文做完整转换。
/// </summary>
internal sealed class MainWindow : Window
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0x4654; // "FT"，进程内唯一即可
    private const int PreviewMaxGraphemes = 64;
    private const int MaxPreviewChars = 160;
    private const int DebounceMs = 150;

    // —— 配色：主色 #6352DC，浅色主题 ——
    private static readonly Brush PrimaryBrush = Frozen(0x63, 0x52, 0xDC);
    private static readonly Brush TextBrush = Frozen(0x25, 0x24, 0x33);
    private static readonly Brush MetaBrush = Frozen(0x77, 0x75, 0x8A);
    private static readonly Brush ChipBackgroundBrush = Frozen(0xE9, 0xE8, 0xF0);
    private static readonly Brush HoverChipBrush = Frozen(0xDD, 0xD9, 0xF5);
    private static readonly Brush HoverItemBrush = Frozen(0xF0, 0xEE, 0xFA);
    private static readonly Brush SelectedItemBrush = Frozen(0xE4, 0xE0, 0xF9);

    private static readonly Dictionary<string, TextStyle> StylesById =
        StyleCatalog.All.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);

    private readonly UsageState _usage;
    private readonly HotkeyBinding _hotkey;
    private readonly DispatcherTimer _debounce;

    private readonly TextBox _inputBox = new();
    private readonly ListBox _listBox = new();
    private readonly TextBlock _statusCount = new();
    private List<StyleListItem> _currentItems = [];
    private FilterOption _filter = FilterOption.All;
    private HwndSource? _hwndSource;
    private bool _hotkeyRegistered;
    private bool _closed;
    private bool _activatedSinceShown; // 防 Show 后未及激活就被 Deactivated"闪没"

    public MainWindow(UsageState usage)
    {
        _usage = usage;
        _hotkey = DesktopConfig.LoadHotkey();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMs) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); RebuildList(); };

        Title = "花式文字";
        Width = 540;
        Height = 640;
        MinWidth = 380;
        MinHeight = 420;
        WindowStyle = WindowStyle.None;     // 无边框弹窗
        ResizeMode = ResizeMode.CanResize;  // 边框不可见，仍可拖边缩放
        Topmost = true;                     // 唤出即置顶
        ShowInTaskbar = false;              // 弹窗不占任务栏
        FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
        Foreground = TextBrush;
        Background = Frozen(0xF6, 0xF6, 0xFA);
        BorderBrush = Frozen(0xD9, 0xD7, 0xE9);
        BorderThickness = new Thickness(1);

        BuildUi();

        PreviewKeyDown += OnPreviewKeyDown;
        Activated += (_, _) => _activatedSinceShown = true;
        Deactivated += OnDeactivated;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        _usage.Changed += OnUsageChanged;

        // 只创建 HWND（挂 WndProc、注册热键），窗口保持隐藏。
        // 注意：EnsureHandle() 不保证触发 SourceInitialized，热键在这里直接注册。
        new WindowInteropHelper(this).EnsureHandle();
        AttachHotkey();
    }

    /// <summary>托盘提示用：当前生效的热键显示文本（如 Ctrl+Alt+F）。</summary>
    internal string HotkeyDisplay => _hotkey.Display;

    // ================================================== 布局（纯代码） ==================================================

    private void BuildUi()
    {
        // 顶部：输入框
        _inputBox.FontSize = 18;
        _inputBox.Padding = new Thickness(10, 8, 10, 8);
        _inputBox.Margin = new Thickness(12, 12, 12, 6);
        _inputBox.VerticalContentAlignment = VerticalAlignment.Center;
        _inputBox.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); }; // 每次击键重置 150ms 防抖

        // 分类筛选：全部 / 收藏 / 最近 / 六个分类，胶囊形单选组
        var filterPanel = new WrapPanel { Margin = new Thickness(12, 0, 12, 4) };
        var options = FilterOption.Catalog().ToArray();
        for (var i = 0; i < options.Length; i++)
        {
            var chip = new RadioButton
            {
                Content = options[i].Label,
                GroupName = "CategoryFilter",
                Tag = options[i],
                Style = CreateChipStyle(),
            };
            if (i == 0)
            {
                chip.IsChecked = true; // 初始"全部"（先设再挂事件，避免构造期触发重建）
            }

            chip.Checked += OnFilterChecked;
            filterPanel.Children.Add(chip);
        }

        // 底部状态栏：左（样式数 + 热键/截断提示），右（快捷键说明）
        _statusCount.FontSize = 11.5;
        _statusCount.Foreground = MetaBrush;
        _statusCount.TextTrimming = TextTrimming.CharacterEllipsis;
        var hints = new TextBlock
        {
            FontSize = 11.5,
            Foreground = MetaBrush,
            Text = "Enter 复制并隐藏 · Esc 隐藏 · Ctrl+D 收藏 · Ctrl+R 换一批随机",
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var status = new DockPanel { Margin = new Thickness(14, 4, 14, 10) };
        DockPanel.SetDock(hints, Dock.Right);
        status.Children.Add(hints);
        status.Children.Add(_statusCount); // 最后一个子元素填充余下空间

        // 主体：样式列表，每项两行（大字预览 + 小字样式名/分类）
        _listBox.BorderThickness = new Thickness(0);
        _listBox.Background = Brushes.Transparent;
        _listBox.Margin = new Thickness(8, 2, 8, 4);
        _listBox.ItemTemplate = CreateItemTemplate();
        _listBox.ItemContainerStyle = CreateItemContainerStyle();
        ScrollViewer.SetHorizontalScrollBarVisibility(_listBox, ScrollBarVisibility.Disabled);
        _listBox.MouseDoubleClick += (_, _) => CommitSelected(); // 双击等价回车

        var root = new DockPanel { Background = Background };
        DockPanel.SetDock(_inputBox, Dock.Top);
        DockPanel.SetDock(filterPanel, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(_inputBox);
        root.Children.Add(filterPanel);
        root.Children.Add(status);
        root.Children.Add(_listBox); // 列表占满余下空间

        Content = root;
    }

    /// <summary>筛选项：全部 / 收藏 / 最近 / 一个具体分类。</summary>
    private sealed record FilterOption(string Label, TextStyleCategory? Category = null, bool Pinned = false, bool Recent = false)
    {
        public static FilterOption All { get; } = new("全部");

        /// <summary>全部 → 收藏 → 最近 → 六个分类（枚举声明顺序），即工具栏顺序。</summary>
        public static IEnumerable<FilterOption> Catalog() =>
        [
            All,
            new FilterOption("收藏", Pinned: true),
            new FilterOption("最近", Recent: true),
            .. Enum.GetValues<TextStyleCategory>().Select(c => new FilterOption(c.DisplayName(), c)),
        ];
    }

    /// <summary>胶囊形单选按钮模板：选中=主色底白字，悬停=浅紫。触发器后声明者优先，故悬停在前、选中在后。</summary>
    private static Style CreateChipStyle()
    {
        var style = new Style(typeof(RadioButton));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 4)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12.5));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "ChipBorder";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
        border.SetValue(Border.BackgroundProperty, ChipBackgroundBrush);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        border.SetValue(Border.PaddingProperty, new Thickness(10, 4, 10, 4));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        // TargetName 的 Setter 只能用于模板触发器；样式触发器只能作用于控件自身属性
        // （原先放在 Style.Triggers 里会在 Seal 时抛 InvalidOperationException，导致整个窗口构建失败）
        var template = new ControlTemplate(typeof(RadioButton)) { VisualTree = border };

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, HoverChipBrush, "ChipBorder"));
        template.Triggers.Add(hover);

        var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, PrimaryBrush, "ChipBorder"));
        checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, PrimaryBrush, "ChipBorder"));
        template.Triggers.Add(checkedTrigger);

        style.Setters.Add(new Setter(Control.TemplateProperty, template));

        var checkedForeground = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        checkedForeground.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Triggers.Add(checkedForeground);

        return style;
    }

    /// <summary>列表项两行数据模板：第一行转换预览（大字号），第二行样式名（⭐ 收藏前缀）+ 分类。</summary>
    private static DataTemplate CreateItemTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));

        var preview = new FrameworkElementFactory(typeof(TextBlock));
        preview.SetBinding(TextBlock.TextProperty, new Binding(nameof(StyleListItem.PreviewText)));
        preview.SetValue(TextBlock.FontSizeProperty, 16d);
        preview.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium);
        preview.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        preview.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 0, 3));

        var meta = new FrameworkElementFactory(typeof(TextBlock));
        meta.SetBinding(TextBlock.TextProperty, new Binding(nameof(StyleListItem.Meta)));
        meta.SetValue(TextBlock.FontSizeProperty, 11.5d);
        meta.SetValue(TextBlock.ForegroundProperty, MetaBrush);
        meta.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);

        panel.AppendChild(preview);
        panel.AppendChild(meta);
        return new DataTemplate(typeof(StyleListItem)) { VisualTree = panel };
    }

    /// <summary>列表项容器：圆角卡片，悬停浅紫、选中淡紫底（淡底保证灰色小字仍可读）。</summary>
    private static Style CreateItemContainerStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 0, 4)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); // 拉满才有 TextTrimming
        style.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "ItemBorder";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.PaddingProperty, new Thickness(10, 7, 10, 7));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        border.AppendChild(presenter);
        style.Setters.Add(new Setter(Control.TemplateProperty,
            new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border }));

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, HoverItemBrush, "ItemBorder"));
        style.Triggers.Add(hover);

        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, SelectedItemBrush, "ItemBorder"));
        style.Triggers.Add(selected);

        return style;
    }

    // ================================================== 唤出 / 隐藏 ==================================================

    /// <summary>唤出弹窗：预填剪贴板 → 重建列表 → 定位到鼠标附近。热键/托盘/二次启动共用入口。</summary>
    public void ShowPopup()
    {
        if (_closed)
        {
            return; // 应用退出中
        }

        if (IsVisible)
        {
            Activate(); // 已打开时只提前
            return;
        }

        SeedInputFromClipboard();
        _debounce.Stop(); // 种入文本已触发过 TextChanged，直接同步重建
        RebuildList();
        PositionNearCursor();
        _activatedSinceShown = false;
        Show();
        Activate();
        _inputBox.Focus();
        _inputBox.SelectAll(); // 打开即可整段替换输入
    }

    /// <summary>预填剪贴板文本并全选；剪贴板为空/被占用/非文本则用内置示例。</summary>
    private void SeedInputFromClipboard()
    {
        var text = string.Empty;
        try
        {
            if (System.Windows.Clipboard.ContainsText())
            {
                text = System.Windows.Clipboard.GetText();
            }
        }
        catch (Exception)
        {
            // 剪贴板被其它进程短暂占用时用默认示例
        }

        _inputBox.Text = string.IsNullOrWhiteSpace(text) ? StyleCatalog.DefaultSample : text;
    }

    /// <summary>在鼠标附近弹出；越过屏幕工作区则翻到另一侧/贴边。定位异常退回主屏居中。</summary>
    private void PositionNearCursor()
    {
        try
        {
            var cursor = WinForms.Cursor.Position; // 物理像素
            var area = WinForms.Screen.FromPoint(cursor).WorkingArea;
            double scale = GetDpiScale();
            double width = (ActualWidth > 0 ? ActualWidth : Width) * scale;
            double height = (ActualHeight > 0 ? ActualHeight : Height) * scale;
            const int margin = 12;

            double x = cursor.X + margin;
            double y = cursor.Y + margin;
            if (x + width > area.Right)
            {
                x = cursor.X - width - margin; // 鼠标右侧放不下 → 放左侧
            }

            if (y + height > area.Bottom)
            {
                y = area.Bottom - height; // 底部放不下 → 贴工作区底边
            }

            x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width));
            y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - height));

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = x / scale;
            Top = y / scale;
        }
        catch (Exception)
        {
            // 多屏/DPI 异常时退回主屏居中
            ClearValue(LeftProperty);
            ClearValue(TopProperty);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    /// <summary>物理像素 → WPF 设备无关单位的缩放比。优先取本窗口实际变换（多屏最准）。</summary>
    private double GetDpiScale()
    {
        var matrix = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
        if (matrix is { } m)
        {
            return m.M11;
        }

        // 首次显示前的兜底：GDI 系统 DPI
        using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
        return g.DpiX / 96.0;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // 失焦即藏。仅在被激活过之后才生效：Windows 前台锁定时 Show 可能拿不到激活，
        // 若一显示就 Deactivated 会把弹窗"闪没"。
        if (_activatedSinceShown)
        {
            Hide();
        }
    }

    // ================================================== 列表构建 ==================================================

    private void OnFilterChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: FilterOption option })
        {
            _filter = option;
            RebuildList();
        }
    }

    private void OnUsageChanged()
    {
        // Changed 可能在任意线程触发（共享状态设计如此），重建列表必须回 UI 线程
        if (Dispatcher.CheckAccess())
        {
            RebuildList();
        }
        else
        {
            Dispatcher.Invoke(RebuildList);
        }
    }

    /// <summary>
    /// 重建列表：预览只转换前 64 个字素；不适用（结果为空/与截断原文相同）的样式不显示，
    /// 与 CLI 的过滤逻辑一致。
    /// </summary>
    private void RebuildList()
    {
        var text = _inputBox.Text;
        var previewInput = TextElementTruncator.Truncate(text, PreviewMaxGraphemes);
        var selectedId = (_listBox.SelectedItem as StyleListItem)?.StyleId;

        List<StyleListItem> items = [];
        foreach (var style in StylesForFilter(_filter))
        {
            string preview;
            try
            {
                preview = style.Transform(previewInput);
            }
            catch (Exception)
            {
                continue; // 单个样式失败不影响整个列表
            }

            // 不适用即隐藏：结果为空（如摩斯之于纯中文），或与原文相同（如纯中文之于拉丁映射）
            if (string.IsNullOrEmpty(preview) || string.Equals(preview, previewInput, StringComparison.Ordinal))
            {
                continue;
            }

            items.Add(new StyleListItem
            {
                StyleId = style.Id,
                Name = style.Name,
                PreviewText = OneLine(preview),
                IsPinned = _usage.IsPinned(style.Id),
                CategoryName = style.Category.DisplayName(),
            });
        }

        _currentItems = items;
        _listBox.ItemsSource = items;

        // 尽量保住原选中（如收藏切换后重建），否则选第一项
        _listBox.SelectedItem = items.FirstOrDefault(i => i.StyleId == selectedId) ?? items.FirstOrDefault();
        if (_listBox.SelectedItem is { } selected)
        {
            _listBox.ScrollIntoView(selected);
        }

        var truncated = !string.Equals(previewInput, text, StringComparison.Ordinal);
        _statusCount.Text = $"共 {items.Count} 个样式 · {_hotkey.Display} 唤出"
            + (truncated ? $" · 预览仅前 {PreviewMaxGraphemes} 字，回车复制完整结果" : string.Empty);
    }

    private IEnumerable<TextStyle> StylesForFilter(FilterOption filter)
    {
        if (filter.Pinned)
        {
            return StylesByIds(_usage.Pinned); // 收藏页保持收藏时间序（最新在前）
        }

        if (filter.Recent)
        {
            return StylesByIds(_usage.Recent);
        }

        if (filter.Category is { } category)
        {
            return StyleCatalog.All.Where(s => s.Category == category);
        }

        return StyleCatalog.All;
    }

    /// <summary>按 ID 列表取回样式（收藏/最近里存的是 ID，样式目录增删后可能失配）。</summary>
    private static IEnumerable<TextStyle> StylesByIds(IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            if (StylesById.TryGetValue(id, out var style))
            {
                yield return style;
            }
        }
    }

    /// <summary>预览单行化并限长，防 Zalgo 之类撑爆测量。</summary>
    private static string OneLine(string text)
    {
        var single = text.ReplaceLineEndings(" ");
        return single.Length <= MaxPreviewChars ? single : single[..MaxPreviewChars] + "…";
    }

    // ================================================== 键盘交互 ==================================================

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Alt 组合键经 Key.System 传递，统一换算后再判断
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        switch (key)
        {
            case Key.Enter:
                if (CommitSelected())
                {
                    e.Handled = true;
                }

                break;
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
            case Key.D when Keyboard.Modifiers == ModifierKeys.Control:
                TogglePinSelected();
                e.Handled = true;
                break;
            case Key.R when Keyboard.Modifiers == ModifierKeys.Control:
                SelectRandom();
                e.Handled = true;
                break;
            case Key.Down when Keyboard.Modifiers == ModifierKeys.None:
                MoveSelection(1);
                e.Handled = true; // 输入框有焦点时也能上下浏览列表
                break;
            case Key.Up when Keyboard.Modifiers == ModifierKeys.None:
                MoveSelection(-1);
                e.Handled = true;
                break;
        }
    }

    /// <summary>回车/双击：对全文做完整转换写入剪贴板 → 记录最近使用 → 隐藏。</summary>
    private bool CommitSelected()
    {
        if (_listBox.SelectedItem is not StyleListItem selected || !StylesById.TryGetValue(selected.StyleId, out var style))
        {
            return false;
        }

        // 复制时才对全文做完整转换（预览只转换了前 64 个字素）
        string result;
        try
        {
            result = style.Transform(_inputBox.Text);
        }
        catch (Exception)
        {
            return false; // 单样式失败静默放弃，不打断使用
        }

        if (string.IsNullOrEmpty(result) || !TryCopyToClipboard(result))
        {
            _statusCount.Text = "剪贴板写入失败，可再按 Enter 重试";
            return false; // 复制失败保持窗口打开，便于重试
        }

        _usage.RecordUse(selected.StyleId);
        Hide();
        return true;
    }

    /// <summary>写入剪贴板：剪贴板可能被其它进程短暂占用，重试 3 次、间隔 100ms。Flush 让数据在本进程退出后仍可粘贴。</summary>
    private static bool TryCopyToClipboard(string text)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                System.Windows.Clipboard.Flush();
                return true;
            }
            catch (Exception)
            {
                if (attempt < 3)
                {
                    Thread.Sleep(100);
                }
            }
        }

        return false;
    }

    private void TogglePinSelected()
    {
        if (_listBox.SelectedItem is StyleListItem selected)
        {
            _usage.TogglePin(selected.StyleId); // Changed → OnUsageChanged 重建列表刷新 ⭐
        }
    }

    /// <summary>从当前筛选结果里随机选一项（换一批）：对 Zalgo 等随机样式尤其有用。</summary>
    private void SelectRandom()
    {
        if (_currentItems.Count == 0)
        {
            return;
        }

        var pick = _currentItems[Random.Shared.Next(_currentItems.Count)];
        _listBox.SelectedItem = pick;
        _listBox.ScrollIntoView(pick);
    }

    private void MoveSelection(int delta)
    {
        var count = _listBox.Items.Count;
        if (count == 0)
        {
            return;
        }

        _listBox.SelectedIndex = Math.Clamp(_listBox.SelectedIndex + delta, 0, count - 1);
        _listBox.ScrollIntoView(_listBox.SelectedItem);
    }

    // ================================================== 全局热键 ==================================================

    private void OnSourceInitialized(object? sender, EventArgs e) => AttachHotkey();

    /// <summary>
    /// 挂 WndProc 并注册全局热键。不依赖 SourceInitialized——EnsureHandle() 在部分场景不触发该事件，
    /// 因此构造函数创建句柄后直接调用；此方法幂等（已注册则跳过）。结果写入诊断日志。
    /// </summary>
    private void AttachHotkey()
    {
        if (_hotkeyRegistered)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            LogDiag("hotkey: 句柄尚未创建，跳过注册");
            return;
        }

        _hwndSource ??= HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);

        // 注册失败（如热键被其它程序占用）不致命：托盘双击仍可唤出
        _hotkeyRegistered = NativeMethods.RegisterHotKey(
            handle,
            HotkeyId,
            _hotkey.Modifiers,
            (uint)KeyInterop.VirtualKeyFromKey(_hotkey.Key));
        LogDiag($"hotkey: 注册 {(_hotkeyRegistered ? "成功" : "失败")} combo={_hotkey.Display} err={Marshal.GetLastWin32Error()}");
    }

    private static void LogDiag(string line)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FancyText", "diag.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} desktop {line}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // 诊断日志失败不影响功能
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt64() == HotkeyId)
        {
            ShowPopup();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        if (_hotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(_hwndSource?.Handle ?? IntPtr.Zero, HotkeyId);
            _hotkeyRegistered = false;
        }

        _hwndSource?.RemoveHook(WndProc);
        _usage.Changed -= OnUsageChanged;
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze(); // 静态画笔冻结，跨线程读取安全
        return brush;
    }
}
