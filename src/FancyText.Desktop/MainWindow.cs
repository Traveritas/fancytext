using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
    private const int WM_NCCALCSIZE = 0x0083;
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
    private static readonly Brush WindowBorderBrush = Frozen(0xE3, 0xE1, 0xF2);
    private static readonly Brush SeparatorBrush = Frozen(0xEF, 0xEE, 0xF6);
    private static readonly Brush ScrollThumbBrush = Frozen(0xCF, 0xCB, 0xE8);

    /// <summary>
    /// 预览字体回退链：花式样式横跨十余个文字系统，单一字体必然出豆腐块。
    /// 依次回退到覆盖符号/古文/南亚/藏文/东南亚文字系统的系统字体（未安装的会被跳过）。
    /// 特意不含 Segoe UI Emoji——Windows 不渲染国旗 emoji，区域指示符 Symbol 即可覆盖，白加载一个大字体。
    /// </summary>
    private const string PreviewFontChain =
        "Microsoft YaHei UI, Segoe UI, Segoe UI Symbol, Segoe UI Historic, " +
        "Nirmala UI, Ebrima, Microsoft Himalaya, Leelawadee UI, Lao UI, Sylfaen";

    private static readonly Dictionary<string, TextStyle> StylesById =
        StyleCatalog.All.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);

    private readonly UsageState _usage;
    private readonly HotkeyBinding _hotkey;
    private readonly DispatcherTimer _debounce;

    private readonly TextBox _inputBox = new();
    private Border _inputBoxBorder = new();
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
        WindowStyle = WindowStyle.None;       // 无边框弹窗（圆角与阴影由 DWM 提供，见 ApplySystemChrome）
        ResizeMode = ResizeMode.CanResize;    // WS_THICKFRAME：可拖边缩放，同时带来 DWM 阴影
        Topmost = true;                       // 唤出即置顶
        ShowInTaskbar = false;                // 弹窗不占任务栏
        FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
        Foreground = TextBrush;
        Background = Frozen(0xFB, 0xFB, 0xFE);

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
        ApplySystemChrome();
    }

    /// <summary>
    /// Win11+：用 DWM 系统圆角与阴影替代 AllowsTransparency+DropShadowEffect——
    /// 位图特效要整面软件渲染缓冲（实测把工作集从 ~70MB 推到 ~190MB，违背轻量化理念），
    /// DWM 方案零额外缓冲；Win10 上该属性为无操作（方角+阴影，观感可接受）。
    /// </summary>
    private void ApplySystemChrome()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
            var preference = 2; // DWMWCP_ROUND
            _ = NativeMethods.DwmSetWindowAttribute(
                handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));

            // Win11 给圆角窗口自带的 1px 描边（顶部最明显，用户观感是"残边"）；
            // NCCALCSIZE 只能去掉非客户区残边，这条是 DWM 画的，必须用边框色属性关掉
            const int DWMWA_BORDER_COLOR = 34;
            var noColor = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
            _ = NativeMethods.DwmSetWindowAttribute(
                handle, DWMWA_BORDER_COLOR, ref noColor, sizeof(int));
        }
        catch (Exception ex)
        {
            LogDiag($"dwm-chrome: 设置圆角失败（不影响使用） {ex.GetType().Name}");
        }
    }

    /// <summary>托盘提示用：当前生效的热键显示文本（如 Ctrl+Alt+F）。</summary>
    internal string HotkeyDisplay => _hotkey.Display;

    // ================================================== 布局（纯代码） ==================================================

    private void BuildUi()
    {
        // 标题栏：应用名 + 提示；空白处按住可拖动窗口
        var header = new DockPanel
        {
            Margin = new Thickness(18, 10, 18, 0),
            Cursor = Cursors.SizeAll,
            Background = Brushes.Transparent, // 命中测试需要非 null 背景
        };
        var title = new TextBlock
        {
            Text = "花式文字",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = PrimaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var headerHint = new TextBlock
        {
            Text = "拖动窗口",
            FontSize = 10,
            Foreground = MetaBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.55,
        };
        DockPanel.SetDock(headerHint, Dock.Right);
        header.Children.Add(headerHint);
        header.Children.Add(title);
        header.MouseLeftButtonDown += OnHeaderDrag;

        // 顶部：输入框。做法：默认 TextBox（IME/编辑器链路保持原生完整——自定义模板曾导致无法输入）
        // 外包一层圆角 Border 拿回观感；聚焦变色走事件，不碰模板。
        _inputBox.FontSize = 18;
        _inputBox.Padding = new Thickness(11, 9, 11, 9);
        _inputBox.VerticalContentAlignment = VerticalAlignment.Center;
        // 注意：输入框不设字体回退链——WPF 会把字体名传给 IME（组合窗口字体），
        // 链式多字体名超过 LOGFONT 32 字符上限是非法名，会导致 IME 组合失败、无法打字（能删不能输）。
        // 预览列表的 TextBlock 不参与 IME，保留完整回退链防豆腐块。
        // _inputBox.FontFamily = new FontFamily(PreviewFontChain);
        _inputBox.BorderThickness = new Thickness(0);
        _inputBox.Background = Brushes.Transparent;
        _inputBox.CaretBrush = PrimaryBrush;
        _inputBox.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); }; // 每次击键重置 150ms 防抖

        _inputBoxBorder = new Border
        {
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            BorderBrush = WindowBorderBrush,
            Background = Frozen(0xF1, 0xF0, 0xF8),
            Padding = new Thickness(2),
            Margin = new Thickness(14, 8, 14, 6),
            Child = _inputBox,
        };
        _inputBox.GotKeyboardFocus += (_, _) =>
        {
            _inputBoxBorder.BorderBrush = PrimaryBrush;
            _inputBoxBorder.Background = Brushes.White;
        };
        _inputBox.LostKeyboardFocus += (_, _) =>
        {
            _inputBoxBorder.BorderBrush = WindowBorderBrush;
            _inputBoxBorder.Background = Frozen(0xF1, 0xF0, 0xF8);
        };

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

        // 底部状态栏：左侧样式数，右侧键帽式快捷键提示（细线分隔，观感对齐现代小工具）
        _statusCount.FontSize = 11.5;
        _statusCount.Foreground = MetaBrush;
        _statusCount.TextTrimming = TextTrimming.CharacterEllipsis;
        _statusCount.VerticalAlignment = VerticalAlignment.Center;
        var hints = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (key, action) in new[] { ("Enter", "复制"), ("Ctrl+D", "收藏"), ("Ctrl+R", "随机"), ("Esc", "收起") })
        {
            if (hints.Children.Count > 0)
            {
                hints.Children.Add(new Border { Width = 9 });
            }

            hints.Children.Add(MakeKeycap(key));
            var label = new TextBlock
            {
                Text = action,
                FontSize = 10.5,
                Foreground = MetaBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
            };
            hints.Children.Add(label);
        }

        var status = new DockPanel
        {
            Margin = new Thickness(16, 8, 16, 12),
            Background = Brushes.Transparent,
        };
        var statusSeparator = new Border
        {
            Height = 1,
            Background = SeparatorBrush,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var statusHost = new DockPanel();
        DockPanel.SetDock(statusSeparator, Dock.Top);
        statusHost.Children.Add(statusSeparator);
        DockPanel.SetDock(hints, Dock.Right);
        statusHost.Children.Add(hints);
        statusHost.Children.Add(_statusCount); // 最后一个子元素填充余下空间
        status.Children.Add(statusHost);

        // 主体：样式列表，每项两行（大字预览 + 小字样式名/分类）
        _listBox.BorderThickness = new Thickness(0);
        _listBox.Background = Brushes.Transparent;
        _listBox.Margin = new Thickness(8, 2, 8, 2);
        _listBox.ItemTemplate = CreateItemTemplate();
        _listBox.ItemContainerStyle = CreateItemContainerStyle();
        ScrollViewer.SetHorizontalScrollBarVisibility(_listBox, ScrollBarVisibility.Disabled);
        _listBox.MouseDoubleClick += (_, _) => CommitSelected(); // 双击等价回车

        // 根容器：DWM 已负责圆角/阴影/描边，这里只承载内容
        var card = new DockPanel { Background = Background };
        card.Resources.Add(typeof(ScrollBar), CreateThinScrollBarStyle()); // 细滚动条全局生效
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_inputBoxBorder, Dock.Top);
        DockPanel.SetDock(filterPanel, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        card.Children.Add(header);
        card.Children.Add(_inputBoxBorder);
        card.Children.Add(filterPanel);
        card.Children.Add(status);
        card.Children.Add(_listBox); // 列表占满余下空间

        Content = card;
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove(); // 显示状态机未就绪时可能抛异常，防御处理
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    /// <summary>细滚动条隐式样式：8px 圆角拇指、无箭头（默认 ScrollBar 过粗，与轻量风格不符）。
    /// 挂到窗口根容器 Resources 后对所有 ListBox/输入框内的滚动条生效。
    /// Track 的 RepeatButton/Thumb 不是依赖属性、工厂模式设不了，模板用 XamlReader 构建最直接。</summary>
    private static Style CreateThinScrollBarStyle()
    {
        const string xaml = """
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                            TargetType="ScrollBar">
              <Grid Background="Transparent">
                <Track x:Name="PART_Track" IsDirectionReversed="True">
                  <Track.DecreaseRepeatButton>
                    <RepeatButton Command="ScrollBar.PageUpCommand" Focusable="False" Background="Transparent" BorderThickness="0" />
                  </Track.DecreaseRepeatButton>
                  <Track.IncreaseRepeatButton>
                    <RepeatButton Command="ScrollBar.PageDownCommand" Focusable="False" Background="Transparent" BorderThickness="0" />
                  </Track.IncreaseRepeatButton>
                  <Track.Thumb>
                    <Thumb Focusable="False">
                      <Thumb.Template>
                        <ControlTemplate TargetType="Thumb">
                          <Border Background="#CFCBE8" CornerRadius="4" Margin="1,2,1,2" />
                        </ControlTemplate>
                      </Thumb.Template>
                    </Thumb>
                  </Track.Thumb>
                </Track>
              </Grid>
            </ControlTemplate>
            """;
        var template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        var style = new Style(typeof(ScrollBar));
        style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 8d));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
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

        var preview = new FrameworkElementFactory(typeof(PreviewTextBlock));
        preview.SetBinding(PreviewTextBlock.TextProperty, new Binding(nameof(StyleListItem.PreviewText)));
        preview.SetValue(TextBlock.FontSizeProperty, 16d);
        preview.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium);
        preview.SetValue(TextBlock.FontFamilyProperty, new FontFamily(PreviewFontChain)); // 花式字符回退链，防豆腐块
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
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.PaddingProperty, new Thickness(12, 8, 12, 8));

        // 选中时左侧出现主色竖条（视觉锚点，比整块高亮更克制）
        var accent = new FrameworkElementFactory(typeof(Border));
        accent.Name = "AccentBar";
        accent.SetValue(Border.WidthProperty, 3d);
        accent.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        accent.SetValue(Border.BackgroundProperty, PrimaryBrush);
        accent.SetValue(Border.MarginProperty, new Thickness(0, 6, 0, 6));
        accent.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        accent.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        accent.SetValue(UIElement.OpacityProperty, 0d); // 默认隐藏，选中触发器点亮
        var accentHost = new FrameworkElementFactory(typeof(Border)); // 叠放层：预览内容之上不占布局
        accentHost.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        accentHost.AppendChild(accent);

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(5, 0, 0, 0));
        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.AppendChild(presenter);
        grid.AppendChild(accentHost);
        border.AppendChild(grid);

        // TargetName 的 Setter 只能放在模板触发器里（Style.Triggers 会抛 InvalidOperationException，
        // 且异常发生在容器生成时——正好把整次 Show() 带崩，窗口完全弹不出来）
        var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border };

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, HoverItemBrush, "ItemBorder"));
        template.Triggers.Add(hover);

        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, SelectedItemBrush, "ItemBorder"));
        selected.Setters.Add(new Setter(UIElement.OpacityProperty, 1d, "AccentBar"));
        template.Triggers.Add(selected);

        style.Setters.Add(new Setter(Control.TemplateProperty, template));

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
            SafeActivate(); // 已打开时只提前
            FocusInput();
            return;
        }

        SeedInputFromClipboard();
        _debounce.Stop(); // 种入文本已触发过 TextChanged，直接同步重建
        RebuildList();
        PositionNearCursor();
        _activatedSinceShown = false;
        Show();

        // 经 EnsureHandle() 预创建句柄的窗口，WPF 的显示状态机在首次/重复唤出时
        // 可能让 Activate() 抛"显示 Window 之前无法调用"——防御处理，不让它吞掉整次唤出
        SafeActivate();
        FocusInput();
    }

    private void SafeActivate()
    {
        try
        {
            Activate();
        }
        catch (InvalidOperationException)
        {
            // Show() 已生效，仅激活失败，无碍
        }
    }

    private void FocusInput()
    {
        try
        {
            _inputBox.Focus();
            _inputBox.SelectAll(); // 打开即可整段替换输入
        }
        catch (Exception)
        {
            // 聚焦失败不影响窗口显示与键盘导航
        }
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
            double scale = GetDpiScale(cursor);
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
            LogDiag($"position: cursor=({cursor.X},{cursor.Y}) scale={scale:F2} area=({area.Left},{area.Top})-({area.Right},{area.Bottom}) sizePhys=({width:F0}x{height:F0}) leftTop=({Left:F0},{Top:F0})");
        }
        catch (Exception)
        {
            // 多屏/DPI 异常时退回主屏居中
            ClearValue(LeftProperty);
            ClearValue(TopProperty);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    /// <summary>
    /// 鼠标所在显示器的 DPI 缩放比（物理像素 / DIU）。逐级回退：光标显示器 → 本窗口变换 → 系统 DPI。
    /// 注意方向：需要的是"DIU→物理"的倍率（TransformToDevice），此前误用 TransformFromDevice
    /// 导致坐标被反向放大——缩放越大、离左上角越远，窗口偏得越厉害。
    /// </summary>
    private double GetDpiScale(System.Drawing.Point cursor)
    {
        try
        {
            var monitor = NativeMethods.MonitorFromPoint(
                new NativeMethods.POINT { X = cursor.X, Y = cursor.Y }, 2 /* MONITOR_DEFAULTTONEAREST */);
            if (monitor != IntPtr.Zero &&
                NativeMethods.GetDpiForMonitor(monitor, 0 /* EFFECTIVE */, out var dpiX, out _) == 0 && dpiX > 0)
            {
                return dpiX / 96.0;
            }
        }
        catch (Exception)
        {
            // shcore 不可用（旧系统）时走回退
        }

        var toDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice;
        if (toDevice is { } matrix)
        {
            return matrix.M11;
        }

        // 显示前的最后兜底：GDI 系统 DPI
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
        _statusCount.Text = $"{items.Count} 个可用样式 · {_hotkey.Display} 唤出"
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
        else if (msg == WM_NCCALCSIZE && wParam.ToInt64() == 1)
        {
            // 无边框窗口保留 WS_THICKFRAME（拖边缩放 + DWM 阴影）时，系统会在顶部画一条残边；
            // 让客户区覆盖整窗即可去掉（标准 borderless 手法，缩放与阴影不受影响）
            handled = true;
            return IntPtr.Zero;
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

    /// <summary>键帽样式的快捷键标签：小圆角边框 + 小字号，比纯文本提示更精致。</summary>
    private static Border MakeKeycap(string label) => new()
    {
        CornerRadius = new CornerRadius(4),
        BorderBrush = Frozen(0xC8, 0xC3, 0xE8),
        BorderThickness = new Thickness(1),
        Background = Frozen(0xEF, 0xED, 0xF9),
        Padding = new Thickness(5, 1.5, 5, 1.5),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = label,
            FontSize = 10,
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = Frozen(0x50, 0x4E, 0x68),
        },
    };

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze(); // 静态画刷冻结，跨线程读取安全
        return brush;
    }

    /// <summary>
    /// 预览专用 TextBlock：把拉丁区组合符（U+0300–036F、U+0483–0489）拆进独立 Run 并显式指定 Arial。
    /// WPF 会把"基字+组合符"整簇锁死在基字的字体上，组合符永远不参与回退（FontProbe 实测：
    /// 字体链/Arial 优先/GlobalUserInterface/自定义 CompositeFont 全部豆腐块，唯一可行是手动按字符拆 Run）。
    /// 这些码点 YaHei/Segoe UI/Symbol 都没有，而 Arial 全覆盖（font-coverage.ps1 枚举 416 个字体验证）。
    /// 藏文/泰文等组合符不动——它们与基字同文字系统，整簇落在链中相应字体内，本就正常。
    /// </summary>
    private sealed class PreviewTextBlock : TextBlock
    {
        private static readonly FontFamily MarkFont = new("Arial");
        private static readonly FontFamily BaseFont = new(PreviewFontChain);
        private bool _building;

        public PreviewTextBlock()
        {
            // TextBlock.OnPropertyChanged 是密封的，只能用描述符监听 Text 变化
            System.ComponentModel.DependencyPropertyDescriptor
                .FromProperty(TextProperty, typeof(TextBlock))
                .AddValueChanged(this, OnTextChanged);
        }

        private void OnTextChanged(object? sender, EventArgs e)
        {
            if (!_building && !string.IsNullOrEmpty(Text))
                BuildRuns();
        }

        private void BuildRuns()
        {
            _building = true;
            try
            {
                Inlines.Clear();
                var buffer = new System.Text.StringBuilder();
                bool current = false; // 当前缓冲段是否为组合符段
                void Flush()
                {
                    if (buffer.Length == 0) return;
                    Inlines.Add(new System.Windows.Documents.Run(buffer.ToString())
                    {
                        FontFamily = current ? MarkFont : BaseFont,
                    });
                    buffer.Clear();
                }
                foreach (var ch in Text)
                {
                    bool mark = (ch >= '\u0300' && ch <= '\u036F') || (ch >= '\u0483' && ch <= '\u0489');
                    if (mark != current)
                    {
                        Flush();
                        current = mark;
                    }
                    buffer.Append(ch);
                }
                Flush();
            }
            finally { _building = false; }
        }
    }
}
