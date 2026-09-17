using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FancyText.Core;
using FancyText.Desktop.Helpers;
using FancyText.Desktop.Models;
using CoreLocalization = FancyText.Core.Localization;

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
    private const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WM_NCCALCSIZE = 0x0083;
    private const int HotkeyId = 0x4654; // "FT"，进程内唯一即可
    private const int PreviewMaxGraphemes = 64;
    private const int MaxPreviewChars = 160;
    private const int DebounceMs = 150;

    /// <summary>当前主题（强调色+明暗）：实例级，设置页改动后整体重建。</summary>
    private Theme _theme;

    /// <summary>当前设置：record 不可变，改动来自设置页（整体替换 + Save + RebuildUi）。</summary>
    private DesktopSettings _settings;

    /// <summary>
    /// 预览字体回退链：花式样式横跨十余个文字系统，单一字体必然出豆腐块。
    /// 依次回退到覆盖符号/古文/南亚/藏文/东南亚文字系统的系统字体（未安装的会被跳过）。
    /// 特意不含 Segoe UI Emoji——Windows 不渲染国旗 emoji，区域指示符 Symbol 即可覆盖，白加载一个大字体。
    /// internal：设置页样式包面板的行内预览共用同一条链（普通 TextBlock，不用本窗私有的 PreviewTextBlock）。
    /// </summary>
    internal const string PreviewFontChain =
        "Microsoft YaHei UI, Segoe UI, Segoe UI Symbol, Segoe UI Historic, " +
        "Nirmala UI, Ebrima, Microsoft Himalaya, Leelawadee UI, Lao UI, Sylfaen";

    /// <summary>样式 ID 索引（内置 + 已安装包），样式包增删后由 <see cref="RefreshStyles"/> 重建。</summary>
    private Dictionary<string, TextStyle> _stylesById =
        StyleCatalog.All.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);

    private readonly UsageState _usage;
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _trimTimer;
    private readonly DispatcherTimer _copyTimer; // 复制反馈：1.2s 后状态栏恢复计数文本
    private HotkeyBinding _hotkey;

    // 可被 BuildUi 整体重建（主题切换时全量换新实例，避免逐控件回填笔刷）
    private DockPanel _card = new();
    private TextBox _inputBox = new();
    private ListBox _listBox = new();
    private TextBlock _statusCount = new();
    private TextBlock _emptyHint = new();
    private ToggleButton _filterChip = new();
    private TextBlock _filterChipLabel = new();
    private ToggleButton _favButton = new();
    private ToggleButton _recentButton = new();
    private Popup _filterPopup = new();
    private DateTime _popupClosedAt = DateTime.MinValue; // 下拉刚关闭的时间点（250ms 内对 chip 的按下视为"再点收起"，见 BuildUi）
    private List<(FilterOption Option, TextBlock Label, TextBlock Check, ToggleButton Item)> _menuItems = [];
    private int _menuKbIndex = -1; // 菜单键盘高亮索引（-1=菜单关闭）；焦点永留输入框，菜单导航走窗口级路由
    private List<StyleListItem> _currentItems = [];
    private FilterOption _filter = FilterOption.All;

    /// <summary>返回行的合成 ID（控制字符开头，不可能与 kebab 样式 ID 相撞）：选中恢复按 StyleId 匹配需要它。</summary>
    private const string BackRowStyleId = "\u0001family-back";

    /// <summary>家族目录钻取状态：非 null 表示正钻在该族里（列表 = 返回行 + 该族全部样式）。</summary>
    private string? _drillFamily;

    private string? _savedSelectedId; // 进输入区前的选中（↓ 回列表区时优先恢复）

    /// <summary>键盘焦点分区：列表区 / 按钮区（chip/⭐/🕘，overlay 高亮）。真实键盘焦点永远留在输入框，
    /// 打字随时回输入；↑↓ 在列表与按钮区间进出，←→ 只在三个按钮间循环。</summary>
    private enum FocusZone { List, Buttons }

    private FocusZone _zone = FocusZone.List;
    private int _buttonZoneIndex; // 0=chip 1=⭐ 2=🕘
    private string _lastCountText = string.Empty; // 计数文本真源：复制提示 1.2s 后原样恢复
    private HwndSource? _hwndSource;
    private bool _hotkeyRegistered;
    private bool _closed;
    private bool _activatedSinceShown; // 防 Show 后未及激活就被 Deactivated"闪没"
    private bool _micaActive;          // Mica 材质生效中（窗口底透明，透出 DWM 系统材质）
    private bool _hiding;              // 退出动画播放中（播完才真正 Hide；期间唤出则取消）

    public MainWindow(UsageState usage)
    {
        _usage = usage;
        _settings = DesktopSettings.Load();
        _hotkey = DesktopSettings.ParseHotkey(_settings.Hotkey) ?? HotkeyBinding.Default;
        _theme = ResolveTheme(_settings);
        UiAnimation.UserPreference = !_settings.ReduceMotion; // 「减少动效」即时生效（BuildUi/模板动画建样式时求值）

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMs) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); RebuildList(); };

        // 隐藏 1.5s 后修剪工作集；期间再唤出则取消（修剪后唤出要靠软缺页换回页面，会慢几十 ms）
        _trimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _trimTimer.Tick += (_, _) => { _trimTimer.Stop(); TrimWorkingSet(); };

        _copyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _copyTimer.Tick += (_, _) => { _copyTimer.Stop(); _statusCount.Text = _lastCountText; };

        Title = Loc.S(Lang, "花式文字", "Fancy Text");
        Width = 540;
        Height = 620;
        MinWidth = 380;
        MinHeight = 420;
        WindowStyle = WindowStyle.None;       // 无边框弹窗（圆角与阴影由 DWM 提供，见 ApplySystemChrome）
        ResizeMode = ResizeMode.CanResize;    // WS_THICKFRAME：可拖边缩放，同时带来 DWM 阴影
        Topmost = true;                       // 唤出即置顶
        ShowInTaskbar = false;                // 弹窗不占任务栏
        FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
        ApplyThemeToWindow();

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

    /// <summary>设置项 → 主题：system 模式读注册表 AppsUseLightTheme（读不到按浅色）；accent=auto 读 DWM 系统强调色。</summary>
    private static Theme ResolveTheme(DesktopSettings settings)
    {
        var dark = settings.Theme switch
        {
            "dark" => true,
            "system" => IsSystemDark(),
            _ => false,
        };
        byte r, g, b;
        if (string.Equals(settings.Accent, "auto", StringComparison.OrdinalIgnoreCase))
        {
            (r, g, b) = Helpers.SystemAccent.TryGet() ?? (0x63, 0x52, 0xDC);
        }
        else if (!DesktopSettings.TryParseColor(settings.Accent, out r, out g, out b))
        {
            (r, g, b) = (0x63, 0x52, 0xDC);
        }

        return new Theme(Color.FromRgb(r, g, b), dark);
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ApplyThemeToWindow()
    {
        Foreground = _theme.Text;
        Background = _micaActive ? Brushes.Transparent : _theme.WindowBackground;
    }

    /// <summary>
    /// Win11+：用 DWM 系统圆角与阴影替代 AllowsTransparency+DropShadowEffect——
    /// 位图特效要整面软件渲染缓冲（实测把工作集从 ~145MB 推到 ~190MB，违背轻量化理念），
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

            ApplyBackdrop(handle);
        }
        catch (Exception ex)
        {
            LogDiag($"dwm-chrome: 设置圆角失败（不影响使用） {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// 背景材质：Mica（Win11 22H2+，Build 22621，且设置 Backdrop=mica）或纯色。
    /// 先设沉浸深色模式（随主题明暗，控制 Mica 深浅着色）；本窗口无边框（NCCALCSIZE 0），
    /// SYSTEMBACKDROP_TYPE 单独设置会"返回成功但什么都不画"——必须先 DwmExtendFrameIntoClientArea(-1)
    /// 把框架区扩展进客户区，材质才有落笔处（失败则还原）。HRESULT==0 才切透明底；
    /// 失败/老系统/纯色：保持主题窗口底色，无感知回退。
    /// Mica 下文字抗锯齿从 ClearType 退化为灰阶（WPF 透明表面已知行为）——设置页留「纯色」逃生门。
    /// </summary>
    private void ApplyBackdrop(IntPtr handle)
    {
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        var dark = _theme.Dark ? 1 : 0;
        _ = NativeMethods.DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        _micaActive = false;
        if (Environment.OSVersion.Version.Build >= 22621 &&
            string.Equals(_settings.Backdrop, "mica", StringComparison.OrdinalIgnoreCase))
        {
            // 无边框（NCCALCSIZE 0）窗口的 SYSTEMBACKDROP 会"返回成功但什么都不画"——
            // 先把框架区扩展进客户区，材质才有落笔处；任一步失败都按 mica 失败处理（防玻璃伪影/透明洞）
            var margins = new NativeMethods.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            var extended = NativeMethods.DwmExtendFrameIntoClientArea(handle, ref margins) == 0;
            const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
            var mica = 2; // DWMSBT_MAINWINDOW
            _micaActive = extended && NativeMethods.DwmSetWindowAttribute(
                handle, DWMWA_SYSTEMBACKDROP_TYPE, ref mica, sizeof(int)) == 0;
        }

        if (!_micaActive)
        {
            // HWND 终身复用：从 Mica 切回/初始化时必须复位 DWM 状态——
            // 否则材质类型与框架扩展残留（DWM 白画一层），交换链残留透明（文字按透明表面走灰阶而非 ClearType）
            const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
            var auto = 0; // DWMSBT_AUTO
            _ = NativeMethods.DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref auto, sizeof(int));
            var zero = new NativeMethods.MARGINS();
            _ = NativeMethods.DwmExtendFrameIntoClientArea(handle, ref zero);
        }

        Background = _micaActive ? Brushes.Transparent : _theme.WindowBackground;
        if ((_hwndSource ?? HwndSource.FromHwnd(handle)) is { } source)
        {
            source.CompositionTarget.BackgroundColor = _micaActive
                ? Colors.Transparent // 客户区透明，透出 DWM 材质
                : ((SolidColorBrush)_theme.WindowBackground).Color; // 复位不透明：恢复 ClearType
        }

        LogDiag($"backdrop: {(_micaActive ? "mica" : "solid")} build={Environment.OSVersion.Version.Build}");
    }

    /// <summary>托盘提示用：当前生效的热键显示文本（如 Ctrl+Alt+F）。</summary>
    internal string HotkeyDisplay => _hotkey.Display;

    /// <summary>设置窗用：当前设置（record 只读快照）。</summary>
    internal DesktopSettings CurrentSettings => _settings;

    /// <summary>设置窗用：当前主题（自身配色的基准）。</summary>
    internal Theme CurrentTheme => _theme;

    /// <summary>设置页样式包预览用：当前输入框文本（弹窗从未唤出过时为空串，调用方自行兜底）。</summary>
    internal string CurrentInput => _inputBox.Text;

    // ================================================== 布局（纯代码，可整体重建） ==================================================

    /// <summary>
    /// 构建全部界面控件。每次都创建新实例并整体替换（主题/设置切换时直接重跑本方法），
    /// 事件全部挂在新控件上，不存在重复挂接；列表内容由调用方随后 RebuildList 填充。
    /// </summary>
    private void BuildUi()
    {
        Title = Loc.S(Lang, "花式文字", "Fancy Text"); // 语言切换重建时随动（构造器里已设过一次）

        // 标题栏：无文字，纯隐形拖动条（按住空白处拖动窗口）
        var header = new DockPanel
        {
            MinHeight = 12,
            Margin = new Thickness(18, 6, 18, 0),
            Cursor = Cursors.SizeAll,
            Background = Brushes.Transparent, // 命中测试需要非 null 背景
        };
        header.MouseLeftButtonDown += OnHeaderDrag;

        // 输入行（E 方向）：放大镜 + 无边框大输入 + 筛选下拉/收藏/最近，行下 1px 细线。
        // 做法：默认 TextBox（IME/编辑器链路保持原生完整——自定义模板曾导致无法输入），
        // 不包圆角盒、不换笔刷；聚焦反馈 = 等宽 accent 线的 Opacity 动画（预着色层，红线合规）。
        _inputBox = new TextBox
        {
            FontSize = 18,
            Padding = new Thickness(2),
            VerticalContentAlignment = VerticalAlignment.Center,
            // 注意：输入框不设字体回退链——WPF 会把字体名传给 IME（组合窗口字体），
            // 链式多字体名超过 LOGFONT 32 字符上限是非法名，会导致 IME 组合失败、无法打字（能删不能输）。
            // 预览列表的 TextBlock 不参与 IME，保留完整回退链防豆腐块。
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = _theme.Text,
            CaretBrush = _theme.Primary,
        };
        _inputBox.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); }; // 每次击键重置 150ms 防抖

        var lang = Lang;
        var ghostStyle = CreateGhostButtonStyle();
        var searchIcon = MakeIconGlyph("\uE721"); // 放大镜
        searchIcon.Foreground = _theme.Meta;
        searchIcon.Margin = new Thickness(2, 0, 8, 0);

        // 筛选状态机：_filter 是唯一真源。点菜单分类 → ⭐/🕘 取消勾选；点图标 → 标签联动；任一变化 → RebuildList。
        var catalog = FilterOption.Catalog(lang).ToArray();
        _filter = catalog.First(o => o.Equals(_filter)); // 换成新语言实例（标签随语言，相等性只看语义键）
        _menuItems = [];

        _filterChipLabel = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        _filterChip = new ToggleButton
        {
            Content = _filterChipLabel,
            Style = ghostStyle, // 幽灵风：透明底（前景色/悬停覆盖层由样式驱动），不再有胶囊块
            Padding = new Thickness(9, 5, 9, 5),
            Background = Brushes.Transparent,
            Focusable = false, // 纯键盘模型：不进 Tab 链（筛选走 Alt+↓ 菜单导航 / Ctrl+Tab 循环），焦点永留输入框
            ToolTip = Loc.S(lang, "筛选分类（Tab 展开 · Ctrl+Tab 循环）", "Filter (Tab to open · Ctrl+Tab to cycle)"),
            Cursor = Cursors.Hand,
            Margin = new Thickness(6, 0, 2, 0),
        };

        // 「全部 ▾」真实下拉：Popup 点窗外自动收起（StaysOpen=false）；仅此小浮层 AllowsTransparency（圆角需要），禁任何 Effect。
        _filterPopup = new Popup
        {
            PlacementTarget = _filterChip,
            Placement = PlacementMode.Custom, // 右对齐 chip 下沿（菜单比 chip 宽，默认左对齐会顶到窗口右边）
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.None,
        };
        _filterPopup.CustomPopupPlacementCallback = (Size popupSize, Size targetSize, Point _) =>
            new[] { new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, targetSize.Height + 6), PopupPrimaryAxis.Horizontal) };

        var menuPanel = new StackPanel();
        foreach (var option in catalog)
        {
            if (option.Pinned || option.Recent)
            {
                continue; // 菜单只放 全部 + 六分类（收藏/最近走图标按钮）
            }

            var label = new TextBlock { Text = option.Label, FontSize = 12, Foreground = _theme.Text, VerticalAlignment = VerticalAlignment.Center };
            var check = new TextBlock { Text = "✓", FontSize = 11, Foreground = _theme.Primary, Opacity = 0, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var content = new DockPanel();
            DockPanel.SetDock(check, Dock.Right);
            content.Children.Add(check);
            content.Children.Add(label);
            var item = new ToggleButton
            {
                Content = content,
                Style = ghostStyle,
                Tag = option,
                Padding = new Thickness(10, 6, 10, 6),
                Background = Brushes.Transparent, // 键盘高亮经 Background 呈现（模板 Grid TemplateBinding 透传）
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Cursor = Cursors.Hand,
            };
            item.Click += (_, _) => ApplyMenuOption(option, item);
            menuPanel.Children.Add(item);
            _menuItems.Add((option, label, check, item));
        }

        _filterPopup.Child = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = _theme.WindowBorder,
            Background = _theme.FlyoutBackground,
            Padding = new Thickness(4),
            MinWidth = 132,
            Child = menuPanel,
        };

        _filterChip.Checked += (_, _) => _filterPopup.IsOpen = true;
        _filterChip.Unchecked += (_, _) => _filterPopup.IsOpen = false;
        // 打开时初始化键盘高亮到当前筛选项；关闭时复位（含点窗外自关闭）
        _filterPopup.Opened += (_, _) =>
        {
            _menuKbIndex = Math.Max(0, _menuItems.FindIndex(m => m.Option.Equals(_filter)));
            ApplyMenuKbHighlight();
        };
        _filterPopup.Closed += (_, _) =>
        {
            _popupClosedAt = DateTime.UtcNow;
            if (_menuKbIndex >= 0)
            {
                SetMenuItemHighlight(_menuKbIndex, false); // 熄灭键盘高亮（overlay Opacity，不动 Background）
                _menuKbIndex = -1;
            }

            if (_filterChip.IsChecked == true)
            {
                _filterChip.IsChecked = false;
            }
        };
        // 菜单开着时再点 chip = 收起。事件顺序坑：StaysOpen=false 的 Popup 在鼠标按下时先自行关闭
        // （Closed 已把 chip 复位），随后这次按下落到 chip 上又会把它勾上重开——
        // 所以不按 IsOpen 判断，而是吞掉"刚关闭 250ms 内"对 chip 的按下。
        _filterChip.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if ((DateTime.UtcNow - _popupClosedAt).TotalMilliseconds < 250)
            {
                e.Handled = true;
            }
        };

        _favButton = new ToggleButton
        {
            Content = MakeIconGlyph("\uE734"), // ⭐ 收藏筛选
            Style = ghostStyle,
            Width = 28,
            Height = 28,
            Background = Brushes.Transparent,
            Focusable = false, // 不进 Tab 链（键盘通道 = Ctrl+Tab 循环里的「收藏」档）
            ToolTip = Loc.S(lang, "收藏筛选", "Pinned filter"),
            Cursor = Cursors.Hand,
            Margin = new Thickness(2, 0, 2, 0),
        };
        _recentButton = new ToggleButton
        {
            Content = MakeIconGlyph("\uE81C"), // 🕘 最近筛选
            Style = ghostStyle,
            Width = 28,
            Height = 28,
            Background = Brushes.Transparent,
            Focusable = false, // 不进 Tab 链（键盘通道 = Ctrl+Tab 循环里的「最近」档）
            ToolTip = Loc.S(lang, "最近筛选", "Recent filter"),
            Cursor = Cursors.Hand,
            Margin = new Thickness(2, 0, 0, 0),
        };
        // Click 只在用户操作时触发（程序改 IsChecked 不触发），天然无重入；再点取消 → 回「全部」
        _favButton.Click += (_, _) =>
        {
            _filter = _favButton.IsChecked == true ? catalog.First(o => o.Pinned) : catalog[0];
            ApplyFilterChange();
        };
        _recentButton.Click += (_, _) =>
        {
            _filter = _recentButton.IsChecked == true ? catalog.First(o => o.Recent) : catalog[0];
            ApplyFilterChange();
        };

        var searchRow = new Grid { Margin = new Thickness(0, 0, 0, 13) }; // 内容与行下细线之间 12px 留白（+1px 线位）
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.Children.Add(searchIcon);
        Grid.SetColumn(_inputBox, 1);
        searchRow.Children.Add(_inputBox);
        Grid.SetColumn(_filterChip, 2);
        searchRow.Children.Add(_filterChip);
        Grid.SetColumn(_favButton, 3);
        searchRow.Children.Add(_favButton);
        Grid.SetColumn(_recentButton, 4);
        searchRow.Children.Add(_recentButton);

        // 行下 1px 细线（Separator）+ 等宽 accent 线（Opacity 0，聚焦时 167ms 淡入）
        var accentLine = new Border
        {
            Height = 1,
            Background = _theme.Primary,
            Opacity = 0,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        var searchBlock = new Grid { Margin = new Thickness(18, 2, 18, 0) };
        searchBlock.Children.Add(searchRow);
        searchBlock.Children.Add(new Border { Height = 1, Background = _theme.Separator, VerticalAlignment = VerticalAlignment.Bottom });
        searchBlock.Children.Add(accentLine);
        searchBlock.Children.Add(_filterPopup); // Popup 自带顶层 HWND，挂树位置无关渲染
        _inputBox.GotKeyboardFocus += (_, _) => UiAnimation.BeginOpacity(accentLine, 1, UiAnimation.SelectMs);
        _inputBox.LostKeyboardFocus += (_, _) => UiAnimation.BeginOpacity(accentLine, 0, UiAnimation.SelectMs);
        SyncFilterVisuals();

        // 底部状态栏：左侧样式数，右侧键帽式快捷键提示（细线分隔，观感对齐现代小工具）
        _statusCount = new TextBlock
        {
            FontSize = 11,
            Foreground = _theme.Meta,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var hints = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var hintPairs = new[]
        {
            ("↑↓", Loc.S(lang, "浏览", "Browse")),
            ("Enter", Loc.S(lang, "复制", "Copy")),
            ("Ctrl+D", Loc.S(lang, "收藏", "Pin")),
            ("Ctrl+R", Loc.S(lang, "随机", "Random")),
            ("Esc", Loc.S(lang, "收起", "Hide")),
        };
        foreach (var (key, action) in hintPairs)
        {
            if (hints.Children.Count > 0)
            {
                hints.Children.Add(new Border { Width = 9 });
            }

            hints.Children.Add(MakeKeycap(key));
            var label = new TextBlock
            {
                Text = action,
                FontSize = 11,
                Foreground = _theme.Meta,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
            };
            hints.Children.Add(label);
        }

        var status = new DockPanel
        {
            Margin = new Thickness(18, 10, 18, 12),
            Background = Brushes.Transparent,
        };
        var statusSeparator = new Border
        {
            Height = 1,
            Background = _theme.Separator,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var statusHost = new DockPanel();
        DockPanel.SetDock(statusSeparator, Dock.Top);
        statusHost.Children.Add(statusSeparator);
        DockPanel.SetDock(hints, Dock.Right);
        statusHost.Children.Add(hints);
        statusHost.Children.Add(_statusCount); // 最后一个子元素填充余下空间
        status.Children.Add(statusHost);

        // 主体：样式列表，单行（左大字预览 + 右侧小字样式名/分类）
        _listBox = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Margin = new Thickness(18, 8, 18, 0),
            ItemTemplate = CreateItemTemplate(),
            ItemContainerStyle = CreateItemContainerStyle(),
            Foreground = _theme.Text,
            FocusVisualStyle = null, // 键盘导航已有选中覆盖层，关掉 WPF 默认的整框虚线焦点矩形
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_listBox, ScrollBarVisibility.Disabled);
        _listBox.MouseDoubleClick += (_, _) => ActivateSelected(); // 双击等价回车（族条目钻入/返回行钻出同分派）

        // 空状态：列表无结果时居中提示（Meta 色，167ms 淡入），不挡命中测试
        _emptyHint = new TextBlock
        {
            FontSize = 12,
            Foreground = _theme.Meta,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false,
            Opacity = 0,
        };
        var listHost = new Grid();
        listHost.Children.Add(_listBox);
        listHost.Children.Add(_emptyHint);

        // 根容器：DWM 已负责圆角/阴影/描边，这里只承载内容；
        // 底色只由 Window.Background 决定（纯色=主题色，Mica=透明透出材质，行为不变）
        var card = new DockPanel { Background = Brushes.Transparent };
        card.Resources.Add(typeof(ScrollBar), CreateThinScrollBarStyle()); // 细滚动条全局生效
        card.RenderTransform = new TranslateTransform(); // 唤出动画预建（播时只改 Y，无布局开销）
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(searchBlock, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        card.Children.Add(header);
        card.Children.Add(searchBlock);
        card.Children.Add(status);
        card.Children.Add(listHost); // 列表占满余下空间

        _card = card;
        Content = card;
    }

    /// <summary>当前界面语言：跟随共享 UsageState（设置页切换、state.json 三端共享），UI 构建时求值。</summary>
    internal AppLanguage Lang => CoreLocalization.Resolve(_usage.Language);

    /// <summary>共享使用状态（设置页读写语言偏好用）。</summary>
    internal UsageState Usage => _usage;

    /// <summary>语言偏好变化（设置页切换）：整体重建 UI。复用设置应用链路（同设置重建，主题值不变无副作用）。</summary>
    internal void ApplyLanguage() => ApplySettings(_settings);

    /// <summary>设置页回调：整体应用新设置（主题/预览字号等）。重建 UI 前保留输入与筛选，
    /// 重建后恢复输入并刷新列表——对用户而言即"即时生效"。</summary>
    internal void ApplySettings(DesktopSettings settings)
    {
        _settings = settings;
        _theme = ResolveTheme(settings);
        UiAnimation.UserPreference = !_settings.ReduceMotion; // 与构造器同步：BuildUi 重建前更新总闸
        ApplyThemeToWindow();
        var input = _inputBox.Text;
        BuildUi();
        _inputBox.Text = input; // 触发防抖重建列表
        if (string.IsNullOrEmpty(input))
        {
            RebuildList();
        }

        ApplySystemChrome(); // 主题明暗/背景材质可能变化：重设 DWM 属性（attr 20/38 随主题重设）

        if (IsVisible)
        {
            // 系统事件触发的跟随重建（强调色/深浅色切换）：文本已保住，焦点与光标一并归还
            _inputBox.Focus();
            _inputBox.CaretIndex = _inputBox.Text.Length;
        }
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
    private Style CreateThinScrollBarStyle()
    {
        var xaml = $$"""
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
                          <Border Background="{{_theme.ScrollThumbHex}}" CornerRadius="4" Margin="1,2,1,2" />
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

    /// <summary>筛选项：全部 / 收藏 / 最近 / 一个具体分类。相等性只看语义键（语言切换重建筛选控件后选中态不丢）。</summary>
    private sealed record FilterOption(string Label, TextStyleCategory? Category = null, bool Pinned = false, bool Recent = false)
    {
        public static FilterOption All { get; } = new("全部");

        /// <summary>全部 → 收藏 → 最近 → 六个分类（枚举声明顺序），即工具栏顺序。</summary>
        public static IEnumerable<FilterOption> Catalog(AppLanguage lang) =>
        [
            new FilterOption(Loc.S(lang, "全部", "All")),
            new FilterOption(Loc.S(lang, "收藏", "Pinned"), Pinned: true),
            new FilterOption(Loc.S(lang, "最近", "Recent"), Recent: true),
            .. Enum.GetValues<TextStyleCategory>().Select(c => new FilterOption(c.DisplayName(lang), c)),
        ];

        public bool Equals(FilterOption? other) =>
            other is not null && Category == other.Category && Pinned == other.Pinned && Recent == other.Recent;

        public override int GetHashCode() => HashCode.Combine(Category, Pinned, Recent);
    }

    /// <summary>图标字体字形（搜索/收藏/最近）：Segoe Fluent Icons 优先，Win10 回退 MDL2。</summary>
    private static TextBlock MakeIconGlyph(string glyph) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        FontSize = 14,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>筛选状态 → 控件视觉同步：chip 标签联动、⭐/🕘 勾选、菜单 ✓ 与强调色。</summary>
    private void SyncFilterVisuals()
    {
        _filterChipLabel.Text = $"{_filter.Label} ▾";
        _favButton.IsChecked = _filter.Pinned;
        _recentButton.IsChecked = _filter.Recent;
        foreach (var (option, label, check, _) in _menuItems)
        {
            var active = option.Equals(_filter);
            check.Opacity = active ? 1 : 0;
            label.Foreground = active ? _theme.Primary : _theme.Text;
        }
    }

    /// <summary>筛选变化统一收口：退出钻取 → 同步视觉 → 重建列表 → 焦点回输入框（对齐设计稿）。</summary>
    private void ApplyFilterChange()
    {
        _drillFamily = null; // 切筛选（胶囊下拉/⭐/🕘 均经此收口）一律回顶层
        SyncFilterVisuals();
        RebuildList();
        _inputBox.Focus();
    }

    /// <summary>Ctrl+Tab / Ctrl+Shift+Tab：按工具栏顺序循环切换筛选（全部→收藏→最近→六分类，环绕）。
    /// 纯键盘通道——三个筛选按钮（下拉/⭐/🕘）的键盘等价物；焦点经收口回到输入框，↑↓ 浏览不中断。</summary>
    private void CycleFilter(int delta)
    {
        var catalog = FilterOption.Catalog(Lang).ToArray();
        var index = Array.FindIndex(catalog, o => o.Equals(_filter));
        index = (index + delta + catalog.Length) % catalog.Length;
        _filter = catalog[index];
        ApplyFilterChange();
    }

    // ---------- 筛选菜单的键盘模型（焦点永留输入框；Popup 是独立 HWND 不收键，由窗口级路由驱动） ----------

    /// <summary>菜单打开期间的按键路由：↑↓ 移高亮、Enter 应用、Esc/Alt+↓ 收起；返回 true=已消费。
    /// 字符等其它键不拦——继续落输入框（菜单保持开着，所见即所得）。</summary>
    private bool HandleMenuKey(Key key, KeyEventArgs e)
    {
        switch (key)
        {
            case Key.Down when Keyboard.Modifiers == ModifierKeys.Alt:
            case Key.Tab: // Tab/Shift+Tab 在菜单开着时 = 收起（焦点遍历在本弹窗里不存在）
            case Key.Escape:
                _filterPopup.IsOpen = false; // Closed 里复位高亮状态
                e.Handled = true;
                return true;
            case Key.Down when Keyboard.Modifiers == ModifierKeys.None:
                MoveMenuHighlight(1);
                e.Handled = true;
                return true;
            case Key.Up when Keyboard.Modifiers == ModifierKeys.None:
                MoveMenuHighlight(-1);
                e.Handled = true;
                return true;
            case Key.Enter:
                if (_menuKbIndex >= 0)
                {
                    ApplyMenuOption(_menuItems[_menuKbIndex].Option, _menuItems[_menuKbIndex].Item);
                }

                e.Handled = true;
                return true;
            default:
                return false;
        }
    }

    /// <summary>菜单键盘高亮（环绕移动）：驱动 ghost 模板里 HoverOverlay 的 Opacity——与鼠标 hover 完全同视觉。
    /// 不要写 Background：恢复时 ClearValue 会把创建时的透明本地值一起清掉，露出 Aero2 默认按钮底。</summary>
    private void MoveMenuHighlight(int delta)
    {
        if (_menuItems.Count == 0)
        {
            return;
        }

        SetMenuItemHighlight(_menuKbIndex, false);
        _menuKbIndex = ((_menuKbIndex + delta) % _menuItems.Count + _menuItems.Count) % _menuItems.Count;
        SetMenuItemHighlight(_menuKbIndex, true);
    }

    private void ApplyMenuKbHighlight() => SetMenuItemHighlight(_menuKbIndex, true);

    /// <summary>打开菜单时惰性解析各菜单项模板里的 HoverOverlay 引用（模板首次渲染后才存在）。</summary>
    private void SetMenuItemHighlight(int index, bool on)
    {
        if (index < 0 || index >= _menuItems.Count)
        {
            return;
        }

        var item = _menuItems[index].Item;
        item.ApplyTemplate();
        if (item.Template?.FindName("HoverOverlay", item) is Border overlay)
        {
            overlay.Opacity = on ? 1 : 0;
        }
    }

    /// <summary>Tab/Shift+Tab/Alt+↓：展开/收起筛选菜单；展开时 Opened 事件把高亮初始化到当前筛选项。</summary>
    private void ToggleFilterMenu()
    {
        if (_filterPopup.IsOpen)
        {
            _filterPopup.IsOpen = false;
        }
        else
        {
            _filterChip.IsChecked = true; // Checked 事件打开菜单
        }
    }

    /// <summary>菜单选定（鼠标点击 / 键盘 Enter 共用）：应用筛选并收起菜单，焦点经收口回输入框。</summary>
    private void ApplyMenuOption(FilterOption option, ToggleButton item)
    {
        _filter = option;
        item.IsChecked = false; // 选中态不走 Toggle，统一由 SyncFilterVisuals 画 ✓
        _filterPopup.IsOpen = false;
        ApplyFilterChange();
    }

    /// <summary>
    /// 覆盖层触发器：条件成立 → 目标元素 Opacity 淡入（Enter=HoldEnd 停终值），退出 → 淡出（Stop 回本地值摘钟）。
    /// 系统动画总闸（<see cref="UiAnimation.Enabled"/>）在 BuildUi 建样式时求值：关闭则退化为瞬时 Setter
    /// （模板触发器的 TargetName Setter 合规；运行期改系统开关随下次 BuildUi 重建生效）。
    /// </summary>
    private static Trigger OverlayTrigger(DependencyProperty property,
        Func<double, FillBehavior, DoubleAnimation> fade, params string[] targets)
    {
        var trigger = new Trigger { Property = property, Value = true };
        if (UiAnimation.Enabled)
        {
            foreach (var target in targets)
            {
                trigger.EnterActions.Add(UiAnimation.OpacityAction(target, fade(1, FillBehavior.HoldEnd)));
                trigger.ExitActions.Add(UiAnimation.OpacityAction(target, fade(0, FillBehavior.Stop)));
            }
        }
        else
        {
            foreach (var target in targets)
            {
                trigger.Setters.Add(new Setter(UIElement.OpacityProperty, 1d, target));
            }
        }

        return trigger;
    }

    /// <summary>
    /// 幽灵按钮模板（筛选 chip / ⭐🕘 图标 / 下拉菜单项共用）：底由 Background 决定，
    /// hover 浅底 = 预着色 HoverItem 覆盖层的 Opacity 83ms 动画（绕开冻结笔刷不能做颜色动画的限制），
    /// IsChecked 时文字变强调色（瞬时 Setter，非颜色过渡）。
    /// </summary>
    private Style CreateGhostButtonStyle()
    {
        var style = new Style(typeof(ToggleButton));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null)); // 关掉默认虚线焦点矩形（自绘模板已有 hover/选中反馈）
        style.Setters.Add(new Setter(Control.ForegroundProperty, _theme.Meta));

        var overlay = new FrameworkElementFactory(typeof(Border));
        overlay.Name = "HoverOverlay";
        overlay.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        overlay.SetValue(Border.BackgroundProperty, _theme.HoverItem);
        overlay.SetValue(UIElement.OpacityProperty, 0d);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.SetValue(Panel.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        grid.AppendChild(overlay);
        grid.AppendChild(presenter);

        var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = grid };
        template.Triggers.Add(OverlayTrigger(UIElement.IsMouseOverProperty, UiAnimation.Fade83, "HoverOverlay"));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));

        // hover 在前、选中在后：同时成立时强调色优先（触发器后声明者优先）
        var hoverForeground = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverForeground.Setters.Add(new Setter(Control.ForegroundProperty, _theme.Text));
        style.Triggers.Add(hoverForeground);
        var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        checkedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, _theme.Primary));
        style.Triggers.Add(checkedTrigger);

        return style;
    }

    /// <summary>列表项单行数据模板（E 方向）：左=转换预览（大字号绝对主角），右=样式名/分类（11px 灰，右对齐）。</summary>
    private DataTemplate CreateItemTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(DockPanel));

        var meta = new FrameworkElementFactory(typeof(PreviewTextBlock.MetaTextBlock));
        meta.SetBinding(PreviewTextBlock.TextProperty, new Binding(nameof(StyleListItem.Meta)));
        meta.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, "Meta"); // UIA 探测脚本按 AutomationId 取行（顺序无关）
        meta.SetValue(TextBlock.FontSizeProperty, 11d);
        meta.SetValue(TextBlock.ForegroundProperty, _theme.Meta);
        meta.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        meta.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 0, 0));
        meta.SetValue(FrameworkElement.MaxWidthProperty, 220d); // 长分类名（含样式包名）不挤爆预览
        meta.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        meta.SetValue(DockPanel.DockProperty, Dock.Right);

        var preview = new FrameworkElementFactory(typeof(PreviewTextBlock));
        preview.SetBinding(PreviewTextBlock.TextProperty, new Binding(nameof(StyleListItem.PreviewText)));
        preview.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, "Preview");
        preview.SetValue(TextBlock.FontSizeProperty, _settings.PreviewFontSize);
        preview.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium);
        preview.SetValue(TextBlock.FontFamilyProperty, new FontFamily(PreviewFontChain)); // 花式字符回退链，防豆腐块
        preview.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        preview.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

        panel.AppendChild(meta);
        panel.AppendChild(preview); // 预览填充余下宽度（TextTrimming 需要受限宽度）
        return new DataTemplate(typeof(StyleListItem)) { VisualTree = panel };
    }

    /// <summary>
    /// 列表项容器（E 方向）：圆角行 + 双层预着色覆盖层——hover 层（83ms）与选中层（167ms）只做 Opacity 动画
    /// （冻结笔刷不能做 ColorAnimation，覆盖层方案是红线合规替代）。
    /// </summary>
    private Style CreateItemContainerStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null)); // 键盘导航已有选中覆盖层，虚线焦点矩形只添乱
        style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 0, 2)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 9, 12, 9)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); // 拉满才有 TextTrimming
        style.Setters.Add(new Setter(Control.ForegroundProperty, _theme.Text));

        var hoverOverlay = new FrameworkElementFactory(typeof(Border));
        hoverOverlay.Name = "HoverOverlay";
        hoverOverlay.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        hoverOverlay.SetValue(Border.BackgroundProperty, _theme.HoverItem);
        hoverOverlay.SetValue(UIElement.OpacityProperty, 0d);

        var selectedOverlay = new FrameworkElementFactory(typeof(Border));
        selectedOverlay.Name = "SelectedOverlay";
        selectedOverlay.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        selectedOverlay.SetValue(Border.BackgroundProperty, _theme.SelectedItem);
        selectedOverlay.SetValue(UIElement.OpacityProperty, 0d);

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));

        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.AppendChild(hoverOverlay);
        grid.AppendChild(selectedOverlay); // 选中层在 hover 层之上（不透明，选中行 hover 不叠加）
        grid.AppendChild(presenter);

        // TargetName 的动画/Setter 只能放在模板触发器里（Style.Triggers 会抛 InvalidOperationException，
        // 且异常发生在容器生成时——正好把整次 Show() 带崩，窗口完全弹不出来）
        var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = grid };
        template.Triggers.Add(OverlayTrigger(UIElement.IsMouseOverProperty, UiAnimation.Fade83, "HoverOverlay"));
        template.Triggers.Add(OverlayTrigger(ListBoxItem.IsSelectedProperty, UiAnimation.Fade167, "SelectedOverlay"));

        style.Setters.Add(new Setter(Control.TemplateProperty, template));

        return style;
    }

    // ================================================== 唤出 / 隐藏 ==================================================

    /// <summary>唤出弹窗：按设置预填 → 重建列表 → 定位（鼠标附近/主屏）。热键/托盘/二次启动共用入口。</summary>
    public void ShowPopup()
    {
        if (_closed)
        {
            return; // 应用退出中
        }

        _trimTimer.Stop(); // 取消待执行的修剪：唤出路径需要全部页面驻留
        CancelHideAnimation(); // 退出动画播到一半被唤出：恢复完整可见，按"已显示"继续

        if (IsVisible)
        {
            SafeActivate(); // 已打开时只提前
            FocusInput();
            return;
        }

        string? prefill = null;
        if (_settings.PrefillSelection)
        {
            prefill = Helpers.SelectedTextReader.TryRead(out var prefillReason);
            if (prefill is not { Length: > 0 } && prefillReason is not null)
            {
                LogDiag($"prefill: UIA 未取到（{prefillReason}）");
            }
        }

        if (prefill is not { Length: > 0 } && _settings.PrefillSelectionPlus)
        {
            // 「加强」兜底：模拟 Ctrl+Insert 复制（UIA 不支持的应用的普适路径），读出即还原剪贴板
            prefill = Helpers.ClipboardCopyReader.TryRead(out var copyReason);
            LogDiag(prefill is { Length: > 0 }
                ? "prefill: 经模拟复制（Ctrl+Insert）取得选中文字"
                : $"prefill: 模拟复制也未取到（{copyReason}）");
        }

        if (prefill is { Length: > 0 })
        {
            _inputBox.Text = prefill; // 其它应用里有选中文字：优先预填（UIA 只读，不动剪贴板）
        }
        else if (_settings.PrefillClipboard)
        {
            SeedInputFromClipboard();
        }
        else if (string.IsNullOrEmpty(_inputBox.Text))
        {
            _inputBox.Text = StyleCatalog.DefaultSample; // 保留上次输入；首次打开给示例
        }

        _debounce.Stop(); // 种入文本已触发过 TextChanged，直接同步重建
        RebuildList();
        if (_settings.PopupPosition == "primary")
        {
            PositionAtPrimary();
        }
        else
        {
            PositionNearCursor();
        }

        _activatedSinceShown = false;

        // 唤出动画起点（终点 = 本地值 1/0；系统动画关闭时 UiAnimation 直接置终点，等价无动画）
        _card.Opacity = 0;
        var rise = _card.RenderTransform as TranslateTransform;
        if (rise is not null)
        {
            rise.Y = -12;
        }

        Show();

        // 经 EnsureHandle() 预创建句柄的窗口，WPF 的显示状态机在首次/重复唤出时
        // 可能让 Activate() 抛"显示 Window 之前无法调用"——防御处理，不让它吞掉整次唤出
        SafeActivate();
        FocusInput(); // 动画期间照常聚焦（立即可输入）

        UiAnimation.BeginOpacity(_card, 1, UiAnimation.SelectMs);
        if (rise is not null)
        {
            UiAnimation.BeginTranslateY(rise, 0, UiAnimation.ShowMs);
        }
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

    /// <summary>主屏工作区居中（设置项"弹窗位置=主屏幕"）。DPI 用主屏中心点探测。</summary>
    private void PositionAtPrimary()
    {
        try
        {
            var screen = WinForms.Screen.PrimaryScreen ?? WinForms.Screen.FromPoint(System.Drawing.Point.Empty);
            var area = screen.WorkingArea;
            var center = new System.Drawing.Point((area.Left + area.Right) / 2, (area.Top + area.Bottom) / 2);
            double scale = GetDpiScale(center);
            double width = (ActualWidth > 0 ? ActualWidth : Width) * scale;
            double height = (ActualHeight > 0 ? ActualHeight : Height) * scale;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = (area.Left + (area.Width - width) / 2) / scale;
            Top = (area.Top + (area.Height - height) / 2) / scale;
        }
        catch (Exception)
        {
            // 兜底：系统居中
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
            HideAndScheduleTrim();
        }
    }

    /// <summary>隐藏并安排工作集修剪（1.5s 后执行，期间唤出则取消）。所有隐藏路径统一走这里。
    /// 退出动画与进入同款（淡出 + 上移，更快一档）：播完才真正 Hide；系统动画关闭时直接隐藏。</summary>
    private void HideAndScheduleTrim()
    {
        _copyTimer.Stop(); // 复制反馈的恢复计时随隐藏作废（隐藏后无活跃时钟）
        if (_hiding || !IsVisible)
        {
            return; // 退出动画播放中/已隐藏：不重复触发
        }

        if (!UiAnimation.Enabled)
        {
            _hiding = true; // 与动画路径一致：FinishHide 的守卫要求置位
            FinishHide();
            return;
        }

        _hiding = true;
        if (_card.RenderTransform is TranslateTransform rise)
        {
            UiAnimation.BeginTranslateY(rise, -12, UiAnimation.HideMs);
        }
        UiAnimation.BeginOpacity(_card, 0, UiAnimation.HideMs, FinishHide);
    }

    /// <summary>退出动画终点：复位动画与终值后真正隐藏（唤出时再设入场起点）。
    /// 守卫：动画期间 ApplySettings 重建换卡 + 唤出打断后，旧时钟的完成回调不得再 Hide。</summary>
    private void FinishHide()
    {
        if (!_hiding)
        {
            return; // 陈旧回调（卡片已被重建/唤出已打断）：丢弃
        }

        _hiding = false;
        _card.BeginAnimation(UIElement.OpacityProperty, null);
        _card.Opacity = 1;
        if (_card.RenderTransform is TranslateTransform rise)
        {
            rise.BeginAnimation(TranslateTransform.YProperty, null);
            rise.Y = 0;
        }

        Hide();
        _trimTimer.Stop();
        _trimTimer.Start();
    }

    /// <summary>唤出打断退出动画：摘掉隐藏动画与回调，恢复完整可见，随后按"已显示"处理。</summary>
    private void CancelHideAnimation()
    {
        if (!_hiding)
        {
            return;
        }

        _hiding = false;
        _card.BeginAnimation(UIElement.OpacityProperty, null);
        _card.Opacity = 1;
        if (_card.RenderTransform is TranslateTransform rise)
        {
            rise.BeginAnimation(TranslateTransform.YProperty, null);
            rise.Y = 0;
        }
    }

    /// <summary>
    /// 轻量化核心手段：EmptyWorkingSet 把工作集整页换出，任务管理器常驻观感压到 ~7-10MB。
    /// 代价是下次唤出要靠软缺页换回页面（慢几十 ms），故只在隐藏一段时间后调用。
    /// </summary>
    private static void TrimWorkingSet()
    {
        try
        {
            var before = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
            _ = NativeMethods.EmptyWorkingSet(NativeMethods.GetCurrentProcess());
            var after = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
            LogDiag($"trim: workingset {before / 1024 / 1024}MB -> {after / 1024 / 1024}MB");
        }
        catch (Exception ex)
        {
            LogDiag($"trim: 失败（不影响功能） {ex.GetType().Name}");
        }
    }

    // ================================================== 列表构建 ==================================================

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

        // 先按现行规则产出平铺样式行（每行带族键，家族折叠/钻取视图在平铺结果上做）
        List<StyleListItem> flat = [];
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

            flat.Add(new StyleListItem
            {
                StyleId = style.Id,
                Name = style.GetName(Lang),
                PreviewText = OneLine(preview),
                IsPinned = _usage.IsPinned(style.Id),
                CategoryName = style.Source is StyleSource.Pack { PackName: var pack }
                    ? $"{style.Category.DisplayName(Lang)} · {pack}"
                    : style.Category.DisplayName(Lang),
                FamilyKey = StyleFamilies.GetFamilyKey(style),
            });
        }

        // 钻取视图 = 返回行 + 该族全部样式（目录序）；族在当前筛选下可见成员不足阈值时自动回落顶层
        // （换筛选/文本大变都会经此求值，故 ShowPopup 等处无需主动清 _drillFamily）
        int? drillCount = null;
        List<StyleListItem> items;
        if (_drillFamily is { } drill && CanGroupFamilies)
        {
            var members = flat.Where(i => i.FamilyKey == drill).ToList();
            if (members.Count >= StyleFamilies.MinMembersToGroup)
            {
                drillCount = members.Count;
                items = [MakeBackRow(drill), .. members];
            }
            else
            {
                _drillFamily = null;
                items = CollapseFamilies(flat);
            }
        }
        else
        {
            _drillFamily = null; // 收藏/最近不支持钻取（永远平铺），走到这里一并兜底清掉
            items = CollapseFamilies(flat);
        }

        _currentItems = items;
        _listBox.ItemsSource = items;

        // 尽量保住原选中（如收藏切换后重建），否则选第一项。
        // 钻入时 selectedId = 族条目的代表样式 ID，天然命中返回行之后的第一个样式行；钻出由 DrillOut 显式恢复。
        _listBox.SelectedItem = items.FirstOrDefault(i => i.StyleId == selectedId) ?? items.FirstOrDefault();
        if (_listBox.SelectedItem is { } selected)
        {
            _listBox.ScrollIntoView(selected);
        }

        // 键盘分区随重建重置为列表区（有无选中皆可：空列表时 ↑ 也能进按钮区）；顺带熄灭按钮区高亮残留
        _zone = FocusZone.List;
        LeaveButtonsZoneVisual();

        var truncated = !string.Equals(previewInput, text, StringComparison.Ordinal);
        if (drillCount is { } n && _drillFamily is { } drillKey)
        {
            var familyName = StyleFamilies.FamilyDisplayName(drillKey, Lang);
            _lastCountText = Loc.S(Lang,
                $"{familyName} · {n} 个样式 · Esc 返回",
                $"{familyName} · {n} styles · Esc back");
        }
        else
        {
            // 计数按可用样式数（flat），不把族条目行算进去——折叠不改变"有多少样式可用"
            _lastCountText = Loc.S(Lang,
                $"{flat.Count} 个可用样式" + (truncated ? $" · 预览仅前 {PreviewMaxGraphemes} 字，回车复制完整结果" : string.Empty),
                $"{flat.Count} styles" + (truncated ? $" · preview shows first {PreviewMaxGraphemes} graphemes; Enter copies the full result" : string.Empty));
        }

        _statusCount.Text = _lastCountText;

        // 空状态：167ms 淡入/淡出（无结果时居中提示，收藏页给专属引导）
        if (items.Count == 0)
        {
            _emptyHint.Text = _filter.Pinned
                ? Loc.S(Lang, "收藏为空，Ctrl+D 收藏常用样式", "No pinned styles yet — Ctrl+D to pin the selected style")
                : Loc.S(Lang, "无可用样式", "No styles available");
        }

        UiAnimation.BeginOpacity(_emptyHint, items.Count == 0 ? 1 : 0, UiAnimation.SelectMs);
    }

    /// <summary>家族折叠仅对「全部/具体分类」启用；收藏/最近永远平铺（列表短，且顺序语义是时间序）。</summary>
    private bool CanGroupFamilies => !_filter.Pinned && !_filter.Recent;

    /// <summary>
    /// 平铺样式行 → 家族折叠：同族可见成员 ≥ <see cref="StyleFamilies.MinMembersToGroup"/> 的折叠成一行族条目
    /// （族条目位置 = 族内首个样式的原位置，保持目录顺序）；不足阈值的族保持平铺。
    /// </summary>
    private List<StyleListItem> CollapseFamilies(List<StyleListItem> flat)
    {
        if (!CanGroupFamilies)
        {
            return flat;
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in flat)
        {
            if (item.FamilyKey is { } key)
            {
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        var emitted = new HashSet<string>(StringComparer.Ordinal);
        List<StyleListItem> items = [];
        foreach (var item in flat)
        {
            if (item.FamilyKey is { } key && counts.GetValueOrDefault(key) >= StyleFamilies.MinMembersToGroup)
            {
                if (emitted.Add(key))
                {
                    items.Add(MakeFamilyRow(key, counts[key], item)); // item 即族内首个（目录序）
                }

                continue; // 其余成员由族条目代收
            }

            items.Add(item);
        }

        return items;
    }

    /// <summary>族条目：StyleId/预览取代表样式（族内第一个），meta 显示成员数与 ▸ 提示可钻入；外观与普通行一致（同模板）。</summary>
    private StyleListItem MakeFamilyRow(string familyKey, int memberCount, StyleListItem representative) => new()
    {
        StyleId = representative.StyleId,
        Name = StyleFamilies.FamilyDisplayName(familyKey, Lang),
        PreviewText = representative.PreviewText,
        IsPinned = false,
        CategoryName = representative.CategoryName,
        Kind = StyleListItem.KindFamily,
        FamilyKey = familyKey,
        MemberCount = memberCount,
        MetaOverride = Loc.S(Lang,
            $"{StyleFamilies.FamilyDisplayName(familyKey, AppLanguage.Chinese)} · {memberCount} 个样式 ▸",
            $"{StyleFamilies.FamilyDisplayName(familyKey, AppLanguage.English)} · {memberCount} styles ▸"),
    };

    /// <summary>钻取视图首行：返回行（预览仅 ‹，meta 留空）。合成 ID 供选中恢复按 StyleId 匹配。</summary>
    private StyleListItem MakeBackRow(string familyKey) => new()
    {
        StyleId = BackRowStyleId,
        Name = Loc.S(Lang, "返回上一级", "Back"),
        PreviewText = "‹",
        IsPinned = false,
        CategoryName = string.Empty,
        Kind = StyleListItem.KindBack,
        FamilyKey = familyKey,
        MetaOverride = Loc.S(Lang, "返回上一级", "Back"), // 行内也要可见（右对齐小字），不只存在 Name 字段里
    };

    /// <summary>钻入家族：列表变为「返回行 + 该族全部样式」。选中经 StyleId 恢复天然命中代表样式行（= 返回行后第一个样式行），兜底显式选首个样式行。</summary>
    private void DrillIn(string familyKey)
    {
        _drillFamily = familyKey;
        RebuildList();
        if (_listBox.SelectedItem is not StyleListItem { Kind: StyleListItem.KindStyle })
        {
            var first = _currentItems.FirstOrDefault(i => i.Kind == StyleListItem.KindStyle);
            if (first is not null)
            {
                _listBox.SelectedItem = first;
                _listBox.ScrollIntoView(first);
            }
        }
    }

    /// <summary>钻出家族：回顶层列表，选中尽量恢复到对应族条目（族此时若已不再折叠则落到默认选中）。</summary>
    private void DrillOut()
    {
        var family = _drillFamily;
        _drillFamily = null;
        RebuildList();
        if (family is not null)
        {
            var row = _currentItems.FirstOrDefault(i => i.Kind == StyleListItem.KindFamily && i.FamilyKey == family);
            if (row is not null)
            {
                _listBox.SelectedItem = row;
                _listBox.ScrollIntoView(row);
            }
        }
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
    private IEnumerable<TextStyle> StylesByIds(IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            if (_stylesById.TryGetValue(id, out var style))
            {
                yield return style;
            }
        }
    }

    /// <summary>样式包导入/卸载后：重建 ID 索引与列表（由设置页调用；插件版需重启其进程）。</summary>
    internal void RefreshStyles()
    {
        _stylesById = StyleCatalog.All.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        RebuildList();
    }

    /// <summary>收藏的样式（导出样式包用），保持收藏时间序。</summary>
    internal IReadOnlyList<TextStyle> PinnedStyles => StylesByIds(_usage.Pinned).ToList();

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

        // 筛选菜单开着：↑↓/Enter/Esc/Alt+↓ 先走菜单导航（焦点永留输入框，见 HandleMenuKey）
        if (_filterPopup.IsOpen && HandleMenuKey(key, e))
        {
            return;
        }

        switch (key)
        {
            case Key.Enter:
                if (_zone == FocusZone.Buttons)
                {
                    ActivateButtonZone(); // 按钮区：chip=开关菜单，⭐/🕘=切换收藏/最近
                    e.Handled = true;
                }
                else if (ActivateSelected()) // 列表/输入区按行类型分派：样式=复制，族条目=钻入，返回行=钻出
                {
                    e.Handled = true;
                }

                break;
            case Key.Escape:
                if (_filterPopup.IsOpen)
                {
                    _filterPopup.IsOpen = false; // 菜单开着先关菜单
                }
                else if (_zone == FocusZone.Buttons)
                {
                    EnterListZone(); // 按钮区 Esc 回列表区（恢复原选中），不收窗口
                }
                else if (_drillFamily is not null)
                {
                    DrillOut(); // 钻取中先返回上一级，再按才隐藏窗口
                }
                else
                {
                    HideAndScheduleTrim();
                }

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
            case Key.Down when Keyboard.Modifiers == ModifierKeys.Alt:
                ToggleFilterMenu(); // Alt+↓ 展开/收起筛选菜单（ComboBox 惯例）
                e.Handled = true;
                break;
            case Key.Down when Keyboard.Modifiers == ModifierKeys.None:
                if (_zone == FocusZone.List)
                {
                    MoveSelection(1); // 输入框有焦点时也能上下浏览列表
                }
                else
                {
                    EnterListZone(); // 按钮区 ↓ 回列表（优先恢复原选中）
                }

                e.Handled = true;
                break;
            case Key.Up when Keyboard.Modifiers == ModifierKeys.None:
                if (_zone == FocusZone.List && _listBox.SelectedIndex <= 0)
                {
                    EnterButtonsZone(0); // 列表到顶再 ↑：进按钮区（chip）
                }
                else if (_zone == FocusZone.Buttons)
                {
                    EnterListZone(); // 按钮区 ↑ 回列表区
                }
                else if (_zone == FocusZone.List)
                {
                    MoveSelection(-1);
                }

                e.Handled = true;
                break;
            case Key.Left when Keyboard.Modifiers == ModifierKeys.None:
                if (_zone == FocusZone.Buttons)
                {
                    MoveButtonZone(-1); // chip ⇄ ⭐ ⇄ 🕘 三键循环
                    e.Handled = true;
                }
                else if (_zone == FocusZone.List && _drillFamily is not null)
                {
                    DrillOut(); // 钻取视图 ← = 返回上一级
                    e.Handled = true;
                }

                break;
            case Key.Right when Keyboard.Modifiers == ModifierKeys.None:
                if (_zone == FocusZone.Buttons)
                {
                    MoveButtonZone(1);
                    e.Handled = true;
                }
                else if (_zone == FocusZone.List &&
                         _listBox.SelectedItem is StyleListItem { Kind: StyleListItem.KindFamily, FamilyKey: { } fam })
                {
                    DrillIn(fam); // 族条目 → = 钻入（Enter 的等价捷径）
                    e.Handled = true;
                }
                else if (_zone == FocusZone.List)
                {
                    EnterButtonsZone(0); // 非族条目 → = 向右进按钮区（chip）
                    e.Handled = true;
                }

                break;
            case Key.Tab when Keyboard.Modifiers == ModifierKeys.Control:
                CycleFilter(1);
                e.Handled = true; // 拦下：否则焦点会移出输入框
                break;
            case Key.Tab when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                CycleFilter(-1);
                e.Handled = true;
                break;
            case Key.Tab: // Tab / Shift+Tab = 展开/收起筛选菜单（本弹窗不做焦点遍历：↑↓ 管列表、打字即回输入框）
                ToggleFilterMenu();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// 回车/双击统一分派：族条目钻入、返回行钻出、样式行复制。
    /// 必须先按 Kind 分派——族条目的 StyleId 是代表样式 ID，直接进 <see cref="CommitSelected"/> 会被误当普通样式复制。
    /// </summary>
    private bool ActivateSelected()
    {
        switch (_listBox.SelectedItem)
        {
            case StyleListItem { Kind: StyleListItem.KindFamily, FamilyKey: { } family }:
                DrillIn(family);
                return true;
            case StyleListItem { Kind: StyleListItem.KindBack }:
                DrillOut();
                return true;
            default:
                return CommitSelected();
        }
    }

    /// <summary>回车/双击：对全文做完整转换写入剪贴板 → 记录最近使用 → 隐藏。</summary>
    private bool CommitSelected()
    {
        if (_listBox.SelectedItem is not StyleListItem selected || !_stylesById.TryGetValue(selected.StyleId, out var style))
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
            _statusCount.Text = Loc.S(Lang, "剪贴板写入失败，可再按 Enter 重试", "Clipboard write failed; press Enter to retry");
            return false; // 复制失败保持窗口打开，便于重试
        }

        _usage.RecordUse(selected.StyleId);

        _copyTimer.Stop();
        _statusCount.Text = _settings.HideAfterCopy
            ? Loc.S(Lang, "已复制 ✓", "Copied ✓")
            : Loc.S(Lang, "已复制 ✓ 可继续换样式（Enter 重复制）", "Copied ✓ pick another style (Enter to copy again)");
        _copyTimer.Start(); // 1.2s 后恢复计数文本（若随即隐藏，HideAndScheduleTrim 会停掉它）
        if (_settings.HideAfterCopy)
        {
            HideAndScheduleTrim(); // 退出动画统一在隐藏路径播放（淡出+上移，与进入同款）
        }

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
        // 仅样式行可收藏：族条目/返回行是导航行，Ctrl+D 对它们不生效
        if (_listBox.SelectedItem is StyleListItem { Kind: StyleListItem.KindStyle } selected)
        {
            _usage.TogglePin(selected.StyleId); // Changed → OnUsageChanged 重建列表刷新 ⭐
        }
    }

    /// <summary>从当前可见行里随机选一个样式（换一批，钻取视图即在该族内随机）：对 Zalgo 等随机样式尤其有用。族条目/返回行是导航行，不参与随机。</summary>
    private void SelectRandom()
    {
        var candidates = _currentItems.Where(i => i.Kind == StyleListItem.KindStyle).ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        var pick = candidates[Random.Shared.Next(candidates.Count)];
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

    // ---------- 键盘焦点分区（列表区/按钮区；真实焦点永留输入框，打字随时回输入） ----------

    /// <summary>进按钮区（chip=0 ⭐=1 🕘=2）：记下当前选中（供回列表恢复）→ 清选中 → 幽灵模板 HoverOverlay 高亮。</summary>
    private void EnterButtonsZone(int index)
    {
        _savedSelectedId = (_listBox.SelectedItem as StyleListItem)?.StyleId ?? _savedSelectedId;
        _listBox.SelectedIndex = -1;
        _zone = FocusZone.Buttons;
        _buttonZoneIndex = index;
        SyncButtonZoneVisual();
    }

    /// <summary>回列表区：优先恢复进按钮区前的选中（按 ID），否则第一项；列表为空则只落分区。</summary>
    private void EnterListZone()
    {
        LeaveButtonsZoneVisual();
        _zone = FocusZone.List;
        if (_currentItems.Count == 0)
        {
            return;
        }

        var target = _currentItems.FirstOrDefault(i => i.StyleId == _savedSelectedId) ?? _currentItems[0];
        _listBox.SelectedItem = target;
        _listBox.ScrollIntoView(target);
    }

    /// <summary>按钮区内循环：chip ⇄ ⭐ ⇄ 🕘（只在这三个之间绕；退出走 ↓/Esc 回列表区）。</summary>
    private void MoveButtonZone(int delta)
    {
        _buttonZoneIndex = ((_buttonZoneIndex + delta) % 3 + 3) % 3;
        SyncButtonZoneVisual();
    }

    /// <summary>按钮区 Enter：chip=开关筛选菜单（按键随后移交菜单路由）；⭐/🕘=切换收藏/最近筛选。</summary>
    private void ActivateButtonZone()
    {
        switch (_buttonZoneIndex)
        {
            case 0:
                LeaveButtonsZoneVisual(); // 菜单接管后续按键（HandleMenuKey 的高亮由菜单自绘）
                ToggleFilterMenu();
                break;
            case 1:
                ToggleSpecialFilter(pinned: true);
                break;
            default:
                ToggleSpecialFilter(pinned: false);
                break;
        }
    }

    /// <summary>⭐/🕘 的键盘等价切换：与图标 Click 同语义（激活 → 对应筛选；再按 → 回「全部」），
    /// 经 ApplyFilterChange 收口（焦点回输入框 + RebuildList 顺带重置分区为列表区）。</summary>
    private void ToggleSpecialFilter(bool pinned)
    {
        var catalog = FilterOption.Catalog(Lang).ToArray();
        var special = catalog.First(o => pinned ? o.Pinned : o.Recent);
        _filter = _filter.Equals(special) ? catalog[0] : special;
        ApplyFilterChange();
    }

    private void SyncButtonZoneVisual()
    {
        SetButtonOverlay(_filterChip, _zone == FocusZone.Buttons && _buttonZoneIndex == 0);
        SetButtonOverlay(_favButton, _zone == FocusZone.Buttons && _buttonZoneIndex == 1);
        SetButtonOverlay(_recentButton, _zone == FocusZone.Buttons && _buttonZoneIndex == 2);
    }

    private void LeaveButtonsZoneVisual()
    {
        SetButtonOverlay(_filterChip, false);
        SetButtonOverlay(_favButton, false);
        SetButtonOverlay(_recentButton, false);
    }

    /// <summary>幽灵按钮的 hover 覆盖层开关（与菜单高亮同一技法：惰性 FindName，不碰 Background）。</summary>
    private static void SetButtonOverlay(ToggleButton button, bool on)
    {
        button.ApplyTemplate();
        if (button.Template?.FindName("HoverOverlay", button) is Border overlay)
        {
            overlay.Opacity = on ? 1 : 0;
        }
    }

    // ================================================== 全局热键 ==================================================

    private void OnSourceInitialized(object? sender, EventArgs e) => AttachHotkey();

    /// <summary>
    /// 换绑全局热键：注销旧键 → 注册新键；新键被占用时回滚注册旧键（用户无感知）。
    /// 返回是否新键生效。托盘提示文字随菜单打开时刷新，无需在此通知。
    /// </summary>
    internal bool TryRebindHotkey(HotkeyBinding binding)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var old = _hotkey;

        if (_hotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(handle, HotkeyId);
            _hotkeyRegistered = false;
        }

        _hotkey = binding;
        _hotkeyRegistered = NativeMethods.RegisterHotKey(
            handle, HotkeyId, binding.Modifiers, (uint)KeyInterop.VirtualKeyFromKey(binding.Key));

        var ok = _hotkeyRegistered;
        if (!ok)
        {
            // 回滚：把旧键注册回去，保持"仍可用旧键唤出"
            _hotkey = old;
            _hotkeyRegistered = NativeMethods.RegisterHotKey(
                handle, HotkeyId, old.Modifiers, (uint)KeyInterop.VirtualKeyFromKey(old.Key));
            LogDiag($"hotkey: 换绑失败 combo={binding.Display} err={Marshal.GetLastWin32Error()}，保留 {old.Display}");
            return false;
        }

        LogDiag($"hotkey: 换绑成功 {old.Display} -> {binding.Display}");
        return true;
    }

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
        else if (msg == WM_DWMCOLORIZATIONCOLORCHANGED && string.Equals(_settings.Accent, "auto", StringComparison.OrdinalIgnoreCase))
        {
            // 系统强调色变化（含"从壁纸自动取色"刷新）：跟随系统模式下即时换肤
            ApplySettings(_settings);
        }
        else if (msg == WM_SETTINGCHANGE && string.Equals(_settings.Theme, "system", StringComparison.OrdinalIgnoreCase))
        {
            // 系统深浅色切换：跟随系统主题模式下即时换肤
            // （WM_SETTINGCHANGE 触发源很多——区域/策略等都会发，明暗真变了才整体重建）
            if (IsSystemDark() != _theme.Dark)
            {
                ApplySettings(_settings);
            }
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
    private Border MakeKeycap(string label) => new()
    {
        CornerRadius = new CornerRadius(4),
        BorderBrush = _theme.KeycapBorder,
        BorderThickness = new Thickness(1),
        Background = _theme.KeycapBackground,
        Padding = new Thickness(5, 1.5, 5, 1.5),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = label,
            FontSize = 10,
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = _theme.KeycapText,
        },
    };

    /// <summary>
    /// 预览专用 TextBlock：把组合符白名单（Unicode 16 Script=Inherited ∪ 实证豆腐段）里的码点
    /// 拆进独立 Run，归宿字体由 FontCoverage 动态解析（GDI 全字体覆盖表查询，启动后台预热）——
    /// 任何码点自动找到真实覆盖它的字体，不再逐区间硬编码。
    /// 机制：WPF 按 script 切 run，Inherited 组合符继承基字 script，整簇锁死在基字字体（CJK=雅黑）
    /// 永不回退 → 豆腐；自带 script 的符号（藏/泰/南亚）WPF 自行切分回退，无需处理（Resolve 返回 null）。
    /// </summary>
    private class PreviewTextBlock : TextBlock
    {
        private static readonly FontFamily BaseFont = new(PreviewFontChain);

        /// <summary>样式名行（meta）的基链：样式名里藏/泰符号与 emoji 兼有，追加 Emoji 兜底
        /// （预览行不追加——白加载大字体；输入框绝不能加——链名超 LOGFONT 32 字符会杀死 IME）。</summary>
        private static readonly FontFamily MetaBaseFont = new(PreviewFontChain + ", Segoe UI Emoji");

        private static readonly Dictionary<string, FontFamily> FontFamilyCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly FontFamily _baseFont;
        private bool _building;

        public PreviewTextBlock() : this(forMeta: false)
        {
        }

        /// <summary>Text 属性描述符（静态缓存同一实例，供订阅/反订阅配对使用）。</summary>
        private static readonly System.ComponentModel.DependencyPropertyDescriptor TextDescriptor =
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextProperty, typeof(TextBlock));

        private bool _subscribed;

        public PreviewTextBlock(bool forMeta)
        {
            _baseFont = forMeta ? MetaBaseFont : BaseFont;
            // TextBlock.OnPropertyChanged 是密封的，只能用描述符监听 Text 变化。
            // 构造即订阅（绑定首次推 Text 前必须就位——Loaded 订阅在部分时序下会错过首轮拆分）；
            // Unloaded（虚拟化回收/BuildUi 重建）摘除防描述符强引用泄漏；Loaded 补订阅并追平错过的文本。
            Subscribe();
            Loaded += (_, _) => Subscribe();
            Unloaded += (_, _) => Unsubscribe();
        }

        private void Subscribe()
        {
            if (!_subscribed)
            {
                TextDescriptor.AddValueChanged(this, OnTextChanged);
                _subscribed = true;
            }
        }

        private void Unsubscribe()
        {
            if (_subscribed)
            {
                TextDescriptor.RemoveValueChanged(this, OnTextChanged);
                _subscribed = false;
            }
        }

        private void OnTextChanged(object? sender, EventArgs e)
        {
            if (!_building && !string.IsNullOrEmpty(Text))
                BuildRuns();
        }

        /// <summary>组合符码点 → 归宿字体（同名缓存同一实例，供 Run 分段按引用比较）；null = 留基链。</summary>
        private static FontFamily? MarkFontFor(char ch)
        {
            var name = FontCoverage.Resolve(ch);
            if (name is null)
            {
                return null;
            }

            if (!FontFamilyCache.TryGetValue(name, out var family))
            {
                family = new FontFamily(name);
                FontFamilyCache[name] = family;
            }

            return family;
        }

        /// <summary>样式名行（meta）专用：基链追加 Segoe UI Emoji（样式名里藏/泰符号与 emoji 兼有）。
        /// FrameworkElementFactory 不支持构造参数，用无参子类路由。</summary>
        internal sealed class MetaTextBlock : PreviewTextBlock
        {
            public MetaTextBlock() : base(forMeta: true)
            {
            }
        }

        private void BuildRuns()
        {
            _building = true;
            try
            {
                Inlines.Clear();
                var buffer = new System.Text.StringBuilder();
                FontFamily? current = null; // 当前缓冲段的标记字体（null = 基链段）
                void Flush()
                {
                    if (buffer.Length == 0) return;
                    Inlines.Add(new System.Windows.Documents.Run(buffer.ToString())
                    {
                        FontFamily = current ?? _baseFont,
                    });
                    buffer.Clear();
                }
                foreach (var ch in Text)
                {
                    var mark = MarkFontFor(ch);
                    if (!ReferenceEquals(mark, current))
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
