using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FancyText.Core;
using FancyText.Desktop.Helpers;
using CoreLocalization = FancyText.Core.Localization;

namespace FancyText.Desktop;

/// <summary>
/// 设置窗口：独立普通窗口（不随弹窗失焦隐藏），全部即时生效——改动即保存并回调主窗口，
/// 免掉"确定/取消"整套状态回滚逻辑（同类工具 PowerToys Run/Flow/uTools 的一致做法）。
/// 主题/强调色变化会重建主弹窗与自身内容。
/// </summary>
internal sealed class SettingsWindow : Window
{
    private const string AutoRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoRunName = "FancyText";

    /// <summary>程序集版本（csproj &lt;Version&gt;），设置页与打包共用。</summary>
    internal static string AppVersion =>
        typeof(SettingsWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0";

    /// <summary>预设强调色：覆盖常见偏好，默认紫在首位。</summary>
    private static readonly string[] AccentPresets =
    [
        "#6352DC", "#0078D4", "#038387", "#107C10", "#986F0B", "#CA5010", "#C42B1C", "#C239B3",
    ];

    private readonly MainWindow _main;
    private DesktopSettings _settings;      // 工作副本：改动即 Save + 应用
    private Theme _theme;
    private AppLanguage _lang;              // UI 构建时求值；语言切换后 BuildContent 重建刷新

    private bool _recordingHotkey;
    private TextBlock _hotkeyHint = new();
    private Button _hotkeyPill = new();
    private TextBlock _packStatus = new();
    private StackPanel _packsList = new();
    private readonly HashSet<string> _expandedPacks = new(StringComparer.OrdinalIgnoreCase); // 面板重填后保留行展开态（按包名）
    private StackPanel _onlineList = new();
    private TextBlock _onlineStatus = new();
    private IReadOnlyList<FancyText.Core.RegistryPackEntry>? _onlineEntries; // 最近一次拉到的索引（null=未拉取）
    private bool _onlineBusy; // 获取/下载期间禁止并发操作

    public SettingsWindow(MainWindow main)
    {
        _main = main;
        _settings = main.CurrentSettings;
        _theme = main.CurrentTheme;
        _lang = CoreLocalization.Resolve(main.Usage.Language);

        Title = Loc.S(_lang, "设置 · 花式文字", "Settings · Fancy Text");
        Width = 470;
        SizeToContent = SizeToContent.Height;
        MinWidth = 430;
        MaxWidth = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
        PreviewKeyDown += OnPreviewKeyDown;

        // 窗口图标 = exe 内嵌 app.ico（标题栏/任务栏/Alt+Tab 跟随品牌化）；无内嵌图标时跳过（dotnet run 开发态）
        using (var extracted = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!))
        {
            if (extracted is not null)
            {
                Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    extracted.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            }
        }

        SourceInitialized += (_, _) => ApplyWindowChrome();

        BuildContent();
    }

    /// <summary>设置窗与主弹窗同一套 DWM 语言：沉浸式深色标题栏随主题（仅系统框架部分，客户区样式自理）。</summary>
    private void ApplyWindowChrome()
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
            var dark = _theme.Dark ? 1 : 0;
            _ = NativeMethods.DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }
        catch (Exception)
        {
            // 老系统无此属性：静默无操作
        }
    }

    private void BuildContent()
    {
        _recordingHotkey = false;
        _lang = CoreLocalization.Resolve(_main.Usage.Language);
        Title = Loc.S(_lang, "设置 · 花式文字", "Settings · Fancy Text"); // 语言切换重建时随动
        Background = _theme.WindowBackground;
        Foreground = _theme.Text;
        var lang = _lang;

        var root = new StackPanel { Margin = new Thickness(24, 18, 24, 20) };

        // —— 个性化 ——
        root.Children.Add(MakeGroupHeader(Loc.S(lang, "个性化", "Personalization")));
        root.Children.Add(MakeRow(Loc.S(lang, "语言", "Language"), MakeLanguageCombo()));
        root.Children.Add(MakeRow(Loc.S(lang, "主题", "Theme"), MakeThemeCombo()));
        root.Children.Add(MakeRow(Loc.S(lang, "背景材质", "Backdrop"), MakeBackdropCombo()));
        root.Children.Add(MakeRow(Loc.S(lang, "强调色", "Accent color"), MakeAccentPanel()));
        root.Children.Add(MakeRow(Loc.S(lang, "预览字号", "Preview size"), MakePreviewSizeCombo()));
        root.Children.Add(MakeRow(Loc.S(lang, "减少动效", "Reduce motion"),
            MakeToggle(nameof(_settings.ReduceMotion), _settings.ReduceMotion, v =>
            {
                Save(_settings with { ReduceMotion = v });
                // 设置窗自身模板的动画时长在建样式时按总闸烘焙——重建才能即时生效（同语言切换的重入规避）
                Dispatcher.BeginInvoke(BuildContent);
            })));

        root.Children.Add(MakeSeparator());

        // —— 快捷键 ——
        root.Children.Add(MakeGroupHeader(Loc.S(lang, "快捷键", "Hotkey")));
        root.Children.Add(MakeHotkeyRow());

        root.Children.Add(MakeSeparator());

        // —— 行为 ——
        root.Children.Add(MakeGroupHeader(Loc.S(lang, "行为", "Behavior")));
        root.Children.Add(MakeRow(Loc.S(lang, "开机自启动", "Launch at login"),
            MakeToggle(nameof(_settings.LaunchAtLogin), _settings.LaunchAtLogin, ApplyLaunchAtLogin)));
        // 子选项联动：兼容模式仅主开关开启时可用（且逻辑上也不生效，见 ShowPopup）
        var prefillPlusToggle = MakeToggle(nameof(_settings.PrefillSelectionPlus), _settings.PrefillSelectionPlus,
            v => Save(_settings with { PrefillSelectionPlus = v }));
        var prefillToggle = MakeToggle(nameof(_settings.PrefillSelection), _settings.PrefillSelection,
            v =>
            {
                Save(_settings with { PrefillSelection = v });
                prefillPlusToggle.IsEnabled = v;
            });
        root.Children.Add(MakeRow(Loc.S(lang, "唤出时预填选中文字", "Prefill selected text"), prefillToggle));
        prefillPlusToggle.IsEnabled = _settings.PrefillSelection;
        var plusRow = MakeRow(Loc.S(lang, "预填兼容模式（模拟复制）", "Compatibility prefill (simulated copy)"), prefillPlusToggle);
        plusRow.ToolTip = Loc.S(lang,
            "部分应用（如某些终端）不支持直接读取选中文字；开启后，预填失败时自动改用 Ctrl+Insert 模拟复制来读取，并立即还原剪贴板（会短暂改动剪贴板）。仅在「唤出时预填选中文字」开启时生效",
            "Some apps (e.g. certain terminals) don't support direct selection reading. When enabled, failed reads fall back to simulating Ctrl+Insert to copy, restoring the clipboard right after (briefly modifies the clipboard). Only effective when 'Prefill selected text' is on");
        root.Children.Add(plusRow);
        root.Children.Add(MakeRow(Loc.S(lang, "唤出时预填剪贴板文字", "Prefill from clipboard"),
            MakeToggle(nameof(_settings.PrefillClipboard), _settings.PrefillClipboard, v => Save(_settings with { PrefillClipboard = v }))));
        root.Children.Add(MakeRow(Loc.S(lang, "复制后收起窗口", "Hide after copying"),
            MakeToggle(nameof(_settings.HideAfterCopy), _settings.HideAfterCopy, v => Save(_settings with { HideAfterCopy = v }))));
        root.Children.Add(MakeRow(Loc.S(lang, "弹窗位置", "Popup position"), MakePositionCombo()));

        root.Children.Add(MakeSeparator());

        // —— 样式包 ——
        root.Children.Add(MakeGroupHeader(Loc.S(lang, "样式包", "Style Packs")));
        root.Children.Add(MakeStylePacksPanel());

        root.Children.Add(MakeSeparator());

        // —— 关于 ——
        root.Children.Add(MakeGroupHeader(Loc.S(lang, "关于", "About")));
        root.Children.Add(MakeRow(Loc.S(lang, "版本", "Version"), MakeMetaText(AppVersion)));
        root.Children.Add(MakeAboutRow());

        var scroll = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 760,
        };
        Content = scroll;
    }

    // ================================================== 控件构造 ==================================================

    private TextBlock MakeGroupHeader(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
        Foreground = _theme.Primary,
        Margin = new Thickness(0, 2, 0, 10),
    };

    private static TextBlock MakeMetaText(string text) => new() { Text = text, FontSize = 12.5, Opacity = 0.8 };

    /// <summary>两列行：左侧标签（定宽），右侧控件（右对齐/填充）。</summary>
    private Border MakeRow(string label, FrameworkElement control)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = _theme.Text,
        };
        DockPanel.SetDock(text, Dock.Left);
        DockPanel.SetDock(control, Dock.Right);
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        row.Children.Add(text);
        row.Children.Add(control); // 最后一个子元素填充余下空间（右缘对齐）
        return new Border { Child = row };
    }

    private Border MakeSeparator() => new()
    {
        Height = 1,
        Background = _theme.Separator,
        Margin = new Thickness(0, 4, 0, 14),
    };

    /// <summary>下拉框深浅色适配：整体模板替换（Aero2 铬板基本不响应 Background/Foreground，深色下直接不可读）。</summary>
    private void StyleCombo(ComboBox combo)
    {
        combo.Foreground = _theme.Text;
        combo.Background = _theme.KeycapBackground;
        combo.Margin = new Thickness(0);
        combo.Style = CreateComboStyle();
        combo.ItemContainerStyle = CreateComboItemStyle();
    }

    /// <summary>只读下拉项：文字/高亮跟随主题（默认白底蓝字在深色主题下完全脱节）。</summary>
    private Style CreateComboItemStyle()
    {
        var style = new Style(typeof(ComboBoxItem));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null)); // 统一去掉虚线焦点矩形
        style.Setters.Add(new Setter(Control.ForegroundProperty, _theme.Text));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 8, 4)));
        var highlighted = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
        highlighted.Setters.Add(new Setter(Control.BackgroundProperty, _theme.HoverItem));
        highlighted.Setters.Add(new Setter(Control.ForegroundProperty, _theme.Text));
        style.Triggers.Add(highlighted);
        return style;
    }

    /// <summary>
    /// 只读下拉框的简洁模板：幽灵按钮（选中项 + ▾，hover 83ms 浅底）+ 圆角浮层列表。
    /// 约定做法：ToggleButton.IsChecked 与 ComboBox.IsDropDownOpen 双向绑定（IsDropDownOpen 是状态真源，
    /// 选中项/点窗外/Esc 的收起由 ComboBox 自身逻辑驱动）；浮层 IsOpen 同样双向绑定（StaysOpen=false
    /// 自关闭要能回写状态）。PART_Popup 命名是 ComboBox 模板的部件约定。
    /// </summary>
    private Style CreateComboStyle()
    {
        var style = new Style(typeof(ComboBox));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null)); // 统一去掉虚线焦点矩形

        // —— 幽灵按钮（显示选中项 + ▾）——
        var selection = new FrameworkElementFactory(typeof(ContentPresenter));
        // 内层是 toggle 模板，TemplateBinding 只能绑 toggle 自身：选中项内容经 toggle.Content 桥接（见下）
        selection.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        selection.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

        var arrow = new FrameworkElementFactory(typeof(TextBlock));
        arrow.SetValue(TextBlock.TextProperty, "▾");
        arrow.SetValue(TextBlock.FontSizeProperty, 11d);
        arrow.SetValue(TextBlock.ForegroundProperty, _theme.Meta);
        arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        arrow.SetValue(DockPanel.DockProperty, Dock.Right);

        var btnContent = new FrameworkElementFactory(typeof(DockPanel));
        btnContent.SetValue(FrameworkElement.MarginProperty, new Thickness(9, 5, 9, 5));
        btnContent.AppendChild(arrow);
        btnContent.AppendChild(selection);

        var btnHover = new FrameworkElementFactory(typeof(Border));
        btnHover.Name = "ComboHover";
        btnHover.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        btnHover.SetValue(Border.BackgroundProperty, _theme.HoverItem);
        btnHover.SetValue(UIElement.OpacityProperty, 0d);

        var btnGrid = new FrameworkElementFactory(typeof(Grid));
        btnGrid.SetValue(Panel.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        btnGrid.AppendChild(btnHover);
        btnGrid.AppendChild(btnContent);

        var toggleTemplate = new ControlTemplate(typeof(ToggleButton)) { VisualTree = btnGrid };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.EnterActions.Add(UiAnimation.OpacityAction("ComboHover", UiAnimation.Fade83(1, FillBehavior.HoldEnd)));
        hover.ExitActions.Add(UiAnimation.OpacityAction("ComboHover", UiAnimation.Fade83(0, FillBehavior.Stop)));
        toggleTemplate.Triggers.Add(hover); // 覆盖层在 toggle 模板命名域里，触发器必须挂同一模板

        var toggle = new FrameworkElementFactory(typeof(ToggleButton));
        toggle.SetValue(ToggleButton.ClickModeProperty, ClickMode.Press);
        // 桥接：内层 toggle 模板里的 TemplateBinding 绑的是 toggle 自身，选中项/底色/文字色先透传到 toggle
        toggle.SetValue(ContentControl.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
        toggle.SetValue(Control.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        toggle.SetValue(Control.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
        toggle.SetValue(ToggleButton.IsCheckedProperty, new Binding(nameof(ComboBox.IsDropDownOpen))
        {
            Mode = BindingMode.TwoWay,
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        toggle.SetValue(Control.TemplateProperty, toggleTemplate);
        toggle.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);

        // —— 圆角浮层列表 ——
        var itemsHost = new FrameworkElementFactory(typeof(ItemsPresenter));
        var scroller = new FrameworkElementFactory(typeof(ScrollViewer));
        scroller.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scroller.SetValue(ScrollViewer.MaxHeightProperty, 360d);
        scroller.AppendChild(itemsHost);

        var flyout = new FrameworkElementFactory(typeof(Border));
        flyout.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        flyout.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        flyout.SetValue(Border.BorderBrushProperty, _theme.WindowBorder);
        flyout.SetValue(Border.BackgroundProperty, _theme.FlyoutBackground);
        flyout.SetValue(Border.PaddingProperty, new Thickness(4));
        flyout.AppendChild(scroller);

        var popup = new FrameworkElementFactory(typeof(Popup));
        popup.Name = "PART_Popup";
        popup.SetValue(Popup.IsOpenProperty, new Binding(nameof(ComboBox.IsDropDownOpen))
        {
            Mode = BindingMode.TwoWay,
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
        popup.SetValue(Popup.AllowsTransparencyProperty, true); // 仅此小浮层（圆角需要），无 Effect
        popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.None);
        popup.SetValue(Popup.StaysOpenProperty, false);
        popup.AppendChild(flyout);

        var root = new FrameworkElementFactory(typeof(Grid));
        root.AppendChild(toggle);
        root.AppendChild(popup);

        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ComboBox)) { VisualTree = root }));
        return style;
    }

    /// <summary>语言下拉：中文 / English / 跟随系统。默认中文（与本地化前版本一致）；切换写入共享 state.json，三端一致。</summary>
    private ComboBox MakeLanguageCombo()
    {
        var combo = new ComboBox { Width = 150, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Right };
        combo.Items.Add("中文");
        combo.Items.Add("English");
        combo.Items.Add(Loc.S(_lang, "跟随系统", "System default"));
        combo.SelectedIndex = _main.Usage.Language switch
        {
            "en" => 1,
            "auto" => 2,
            _ => 0,
        };
        StyleCombo(combo);
        combo.SelectionChanged += (_, _) =>
        {
            var stored = combo.SelectedIndex switch { 1 => "en", 2 => "auto", _ => "zh" };
            _main.Usage.SetLanguage(stored);
            // 与 retheme 同理：事件处理器内替换自身控件树有重入风险，延迟到空闲；主弹窗即时重建
            Dispatcher.BeginInvoke(BuildContent);
            _main.ApplyLanguage();
        };
        return combo;
    }

    private ComboBox MakeThemeCombo()
    {
        var combo = new ComboBox { Width = 150, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Right };
        combo.Items.Add(Loc.S(_lang, "浅色", "Light"));
        combo.Items.Add(Loc.S(_lang, "深色", "Dark"));
        combo.Items.Add(Loc.S(_lang, "跟随系统", "System"));
        combo.SelectedIndex = _settings.Theme switch
        {
            "dark" => 1,
            "system" => 2,
            _ => 0,
        };
        StyleCombo(combo);
        combo.SelectionChanged += (_, _) =>
        {
            var theme = combo.SelectedIndex == 1 ? "dark" : combo.SelectedIndex == 2 ? "system" : "light";
            Save(_settings with { Theme = theme }, retheme: true);
        };
        return combo;
    }

    /// <summary>背景材质：Win11 默认跟随系统 Mica（DWM 系统材质，文字 ClearType 在透明表面上退化为灰阶）；
    /// 纯色退回纯主题色背景，作为观感逃生门。</summary>
    private ComboBox MakeBackdropCombo()
    {
        var combo = new ComboBox { Width = 150, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Right };
        combo.Items.Add(Loc.S(_lang, "跟随系统 Mica", "System Mica"));
        combo.Items.Add(Loc.S(_lang, "纯色", "Solid"));
        combo.SelectedIndex = _settings.Backdrop == "solid" ? 1 : 0;
        StyleCombo(combo);
        combo.SelectionChanged += (_, _) =>
            Save(_settings with { Backdrop = combo.SelectedIndex == 1 ? "solid" : "mica" });
        return combo;
    }

    private ComboBox MakePreviewSizeCombo()
    {
        var combo = new ComboBox { Width = 150, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Right };
        combo.Items.Add(Loc.S(_lang, "小", "Small"));
        combo.Items.Add(Loc.S(_lang, "中", "Medium"));
        combo.Items.Add(Loc.S(_lang, "大", "Large"));
        combo.SelectedIndex = _settings.PreviewSize switch
        {
            "small" => 0,
            "large" => 2,
            _ => 1,
        };
        StyleCombo(combo);
        combo.SelectionChanged += (_, _) => Save(_settings with
        {
            PreviewSize = combo.SelectedIndex == 0 ? "small" : combo.SelectedIndex == 2 ? "large" : "medium",
        });
        return combo;
    }

    private ComboBox MakePositionCombo()
    {
        var combo = new ComboBox { Width = 150, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Right };
        combo.Items.Add(Loc.S(_lang, "鼠标所在屏幕", "Screen at cursor"));
        combo.Items.Add(Loc.S(_lang, "主屏幕居中", "Centered on primary"));
        combo.SelectedIndex = _settings.PopupPosition == "primary" ? 1 : 0;
        StyleCombo(combo);
        combo.SelectionChanged += (_, _) =>
            Save(_settings with { PopupPosition = combo.SelectedIndex == 1 ? "primary" : "cursor" });
        return combo;
    }

    /// <summary>Fluent 风开关（替换默认方框 CheckBox）：圆角轨道 + 圆形滑块，勾选时滑块 83ms 滑入
    /// 且轨道变强调色（「减少动效」/系统动画关闭时 0ms 瞬时到位）。对外仍是 IsChecked/Checked/Unchecked。</summary>
    private ToggleButton MakeToggle(string name, bool value, Action<bool> apply)
    {
        var box = new ToggleButton
        {
            IsChecked = value,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = name,
            Style = CreateSwitchStyle(),
            Cursor = Cursors.Hand,
        };
        box.Checked += (_, _) => apply(true);
        box.Unchecked += (_, _) => apply(false);
        return box;
    }

    /// <summary>开关模板：轨道 38×18 圆角 9（未选=透明底+Meta 描边、选中=强调色），滑块 12px
    /// 白圆随 IsChecked 滑动 0→20px。笔刷切换走瞬时 Setter（冻结笔刷不能动画），滑动只动 RenderTransform。</summary>
    private Style CreateSwitchStyle()
    {
        var style = new Style(typeof(ToggleButton));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null)); // 统一去掉虚线焦点矩形

        var track = new FrameworkElementFactory(typeof(Border));
        track.Name = "Track";
        track.SetValue(Border.WidthProperty, 38d);
        track.SetValue(Border.HeightProperty, 18d);
        track.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        track.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        track.SetValue(Border.BorderBrushProperty, _theme.Meta);
        track.SetValue(Border.BackgroundProperty, Brushes.Transparent);

        var thumb = new FrameworkElementFactory(typeof(Border));
        thumb.Name = "Thumb";
        thumb.SetValue(Border.WidthProperty, 12d);
        thumb.SetValue(Border.HeightProperty, 12d);
        thumb.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        thumb.SetValue(Border.BackgroundProperty, _theme.Meta);
        thumb.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        thumb.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        thumb.SetValue(FrameworkElement.MarginProperty, new Thickness(3, 0, 0, 0));
        thumb.SetValue(UIElement.RenderTransformProperty, new TranslateTransform());

        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.AppendChild(track);
        grid.AppendChild(thumb);

        var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = grid };

        var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        on.EnterActions.Add(SwitchSlideAction(on: true));
        on.ExitActions.Add(SwitchSlideAction(on: false));
        on.Setters.Add(new Setter(Border.BackgroundProperty, _theme.Primary, "Track"));
        on.Setters.Add(new Setter(Border.BorderBrushProperty, _theme.Primary, "Track"));
        on.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.White, "Thumb"));
        template.Triggers.Add(on);

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, _theme.Text, "Track"));
        template.Triggers.Add(hover);

        // 禁用态：整键降透明度（自绘模板没有系统灰化，子选项联动禁用时必须有视觉反馈）
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
        template.Triggers.Add(disabled);

        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    /// <summary>滑块滑动故事板：TranslateTransform.X → on?20:0；「减少动效」时 0ms 等价瞬时到位。</summary>
    private static BeginStoryboard SwitchSlideAction(bool on)
    {
        var animation = new DoubleAnimation(on ? 20d : 0d,
            TimeSpan.FromMilliseconds(UiAnimation.Enabled ? UiAnimation.HoverMs : 0))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = on ? FillBehavior.HoldEnd : FillBehavior.Stop, // on 停在 20；off 回本地值 0 并摘钟（不留挂钟）
        };
        Storyboard.SetTargetName(animation, "Thumb");
        Storyboard.SetTargetProperty(animation, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        return new BeginStoryboard { Storyboard = storyboard };
    }

    /// <summary>统一小按钮：主题底色/文字色（默认白按钮在深色主题下过亮）+ 圆角 hover 模板（直角铬板与新弹窗割裂）。</summary>
    private void StyleButton(Button button)
    {
        button.Foreground = _theme.KeycapText;
        button.Background = _theme.KeycapBackground;
        button.BorderBrush = _theme.KeycapBorder;
        button.BorderThickness = new Thickness(1);
        button.Style = CreateButtonStyle();
    }

    /// <summary>按钮模板：圆角 6，hover 时预着色 HoverItem 覆盖层淡入（83ms，「减少动效」时 0ms 瞬时）。
    /// 颜色/边框/内距经 TemplateBinding 透传实例值（StyleButton 设定）。</summary>
    private Style CreateButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null)); // 统一去掉虚线焦点矩形

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "BtnBorder";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));

        var hoverOverlay = new FrameworkElementFactory(typeof(Border));
        hoverOverlay.Name = "BtnHover";
        hoverOverlay.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        hoverOverlay.SetValue(Border.BackgroundProperty, _theme.HoverItem);
        hoverOverlay.SetValue(UIElement.OpacityProperty, 0d);

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.AppendChild(hoverOverlay);
        grid.AppendChild(presenter);
        border.AppendChild(grid);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.EnterActions.Add(UiAnimation.OpacityAction("BtnHover", UiAnimation.Fade83(1, FillBehavior.HoldEnd)));
        hover.ExitActions.Add(UiAnimation.OpacityAction("BtnHover", UiAnimation.Fade83(0, FillBehavior.Stop)));
        template.Triggers.Add(hover);

        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    /// <summary>强调色面板：「跟随系统」胶囊 + 8 个预设圆点 + 自定义十六进制输入。</summary>
    private StackPanel MakeAccentPanel()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var swatches = new StackPanel { Orientation = Orientation.Horizontal };

        // 跟随系统：胶囊按钮显示当前系统强调色的小色块，选中（Accent=auto）时描边
        var isAuto = string.Equals(_settings.Accent, "auto", StringComparison.OrdinalIgnoreCase);
        var autoChip = new Border
        {
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            BorderBrush = isAuto ? _theme.Text : _theme.KeycapBorder,
            Background = _theme.KeycapBackground,
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Loc.S(_lang, "使用 Windows 系统强调色（设置 → 个性化 → 颜色），系统改色后自动跟随", "Uses the Windows system accent color (Settings → Personalization → Color); follows system changes"),
        };
        var autoDot = new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(ParseColor("#6352DC")),
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (Helpers.SystemAccent.TryGet() is { } accent)
        {
            autoDot.Background = new SolidColorBrush(Color.FromRgb(accent.R, accent.G, accent.B));
        }

        var autoLabel = new TextBlock
        {
            Text = Loc.S(_lang, "跟随系统", "System"),
            FontSize = 11,
            Foreground = _theme.Text,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var autoRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        autoRow.Children.Add(autoDot);
        autoRow.Children.Add(autoLabel);
        autoChip.Child = autoRow;
        autoChip.MouseLeftButtonUp += (_, _) => Save(_settings with { Accent = "auto" }, retheme: true);
        swatches.Children.Add(autoChip);

        foreach (var hex in AccentPresets)
        {
            var hexCapture = hex;
            var dot = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(ParseColor(hex)),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(2),
                BorderBrush = !isAuto && string.Equals(_settings.Accent, hexCapture, StringComparison.OrdinalIgnoreCase)
                    ? _theme.Text
                    : Brushes.Transparent,
                ToolTip = hexCapture,
            };
            dot.MouseLeftButtonUp += (_, _) => Save(_settings with { Accent = hexCapture }, retheme: true);
            swatches.Children.Add(dot);
        }

        var hexBox = new TextBox
        {
            Width = 90,
            FontSize = 12,
            Padding = new Thickness(6, 3, 6, 3),
            Text = isAuto ? string.Empty : _settings.Accent,
            IsReadOnly = isAuto, // 跟随系统时不接受手输，点色板或胶囊退出该模式
            VerticalAlignment = VerticalAlignment.Center,
            // 主题化：默认白底输入框在深色主题下是视觉噪点
            Background = _theme.InputIdleBackground,
            Foreground = _theme.Text,
            BorderBrush = _theme.WindowBorder,
            CaretBrush = _theme.Primary,
        };
        var hexHint = new TextBlock
        {
            Text = isAuto ? Loc.S(_lang, "跟随系统中 ✓", "Following system ✓") : "",
            FontSize = 10.5,
            Foreground = _theme.Primary,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        var applyBtn = new Button
        {
            Content = Loc.S(_lang, "应用", "Apply"),
            FontSize = 12,
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
        };
        StyleButton(applyBtn);
        applyBtn.Click += (_, _) =>
        {
            if (DesktopSettings.TryParseColor(hexBox.Text, out _, out _, out _))
            {
                hexHint.Text = "";
                hexBox.ClearValue(Border.BorderBrushProperty);
                Save(_settings with { Accent = hexBox.Text.Trim().ToUpperInvariant() }, retheme: true);
            }
            else
            {
                hexHint.Text = Loc.S(_lang, "格式：#RRGGBB", "Format: #RRGGBB");
                hexBox.BorderBrush = Frozen(0xD1, 0x3B, 0x3B);
            }
        };

        var hexPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        hexPanel.Children.Add(hexBox);
        hexPanel.Children.Add(applyBtn);
        hexPanel.Children.Add(hexHint);

        var vertical = new StackPanel();
        vertical.Children.Add(swatches);
        vertical.Children.Add(hexPanel);
        panel.Children.Add(vertical);
        return panel;
    }

    /// <summary>热键行：录制胶囊 + 状态提示。录制惯例：点击进入录制态 → 按组合键实时回显 → Esc 取消；
    /// 占用判定以 RegisterHotKey 注册成败为准（失败自动回滚旧热键，零感知）。</summary>
    private StackPanel MakeHotkeyRow()
    {
        _hotkeyPill = new Button
        {
            Content = _main.HotkeyDisplay,
            MinWidth = 150,
            FontSize = 12.5,
            Padding = new Thickness(12, 5, 12, 5),
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = Cursors.Hand,
        };
        StyleButton(_hotkeyPill);
        _hotkeyPill.Click += (_, _) =>
        {
            _recordingHotkey = true;
            _hotkeyPill.Content = Loc.S(_lang, "按下组合键…", "Press a key combo…");
            _hotkeyPill.FontWeight = FontWeights.SemiBold;
            _hotkeyHint.Text = Loc.S(_lang, "Esc 取消", "Esc to cancel");
            _hotkeyHint.Foreground = _theme.Meta;
        };

        _hotkeyHint = new TextBlock
        {
            Text = "",
            FontSize = 10.5,
            Foreground = _theme.Meta,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(_hotkeyPill);
        panel.Children.Add(_hotkeyHint);
        return panel;
    }

    private StackPanel MakeAboutRow()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FancyText");
        var text = new TextBlock
        {
            Text = path,
            FontSize = 11,
            Opacity = 0.8,
            Foreground = _theme.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 220,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var button = new Button
        {
            Content = Loc.S(_lang, "打开文件夹", "Open folder"),
            FontSize = 12,
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand,
        };
        StyleButton(button);
        button.Click += (_, _) =>
        {
            try
            {
                Process.Start("explorer.exe", path);
            }
            catch (Exception)
            {
                // 打开失败静默（路径始终存在于提示里）
            }
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        row.Children.Add(text);
        row.Children.Add(button);
        var host = new StackPanel();
        host.Children.Add(row);
        return host;
    }

    /// <summary>
    /// 样式包管理面板：顶部导入（.json 包文件 → %LOCALAPPDATA%\FancyText\styles）/导出收藏按钮 + 状态行，
    /// 下方两组——「官方样式包」（exe 内嵌，一键安装/启停/卸载）与「已安装样式包」（用户导入，启停/卸载，
    /// 损坏标红只能卸载）。每行可展开，用主窗当前输入逐样式做单行转换预览。
    /// 任何动作后即时刷新主窗口列表；命令面板插件需重启其进程后生效。
    /// </summary>
    private StackPanel MakeStylePacksPanel()
    {
        var importBtn = new Button
        {
            Content = Loc.S(_lang, "导入样式包…", "Import pack…"),
            FontSize = 12,
            Padding = new Thickness(10, 3, 10, 3),
            Cursor = Cursors.Hand,
        };
        StyleButton(importBtn);
        importBtn.Click += (_, _) => OnImportPack();

        var exportBtn = new Button
        {
            Content = Loc.S(_lang, "导出收藏为包…", "Export pinned…"),
            FontSize = 12,
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand,
        };
        StyleButton(exportBtn);
        exportBtn.Click += (_, _) => OnExportPinnedPack();

        var fetchBtn = new Button
        {
            Content = Loc.S(_lang, "获取更多样式包…", "Get more packs…"),
            FontSize = 12,
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand,
        };
        StyleButton(fetchBtn);
        fetchBtn.Click += (_, _) => OnFetchOnlinePacks();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        _packStatus = new TextBlock
        {
            Text = "",
            FontSize = 10.5,
            Foreground = _theme.Meta,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        _packsList = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        FillPacksPanel();

        _onlineStatus = new TextBlock
        {
            Text = "",
            FontSize = 10.5,
            Foreground = _theme.Meta,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        _onlineList.Visibility = Visibility.Collapsed;
        _onlineStatus.Visibility = Visibility.Collapsed;
        var onlineSection = new StackPanel();
        onlineSection.Children.Add(MakePackGroupHeader(Loc.S(_lang, "在线获取（GitHub 索引）", "Online (GitHub index)")));
        onlineSection.Children.Add(_onlineList);
        onlineSection.Children.Add(_onlineStatus);

        var panel = new StackPanel();
        buttons.Children.Add(importBtn);
        buttons.Children.Add(exportBtn);
        buttons.Children.Add(fetchBtn);
        panel.Children.Add(buttons);
        panel.Children.Add(_packStatus);
        panel.Children.Add(_packsList);
        panel.Children.Add(onlineSection);
        return panel;
    }

    /// <summary>拉取在线样式包索引：主通道 raw.githubusercontent.com，失败回退 jsDelivr 镜像；结果渲染进可折叠的在线区。</summary>
    private async void OnFetchOnlinePacks()
    {
        if (_onlineBusy)
        {
            return;
        }

        _onlineBusy = true;
        _onlineList.Visibility = Visibility.Visible;
        _onlineStatus.Visibility = Visibility.Visible;
        _onlineStatus.Foreground = _theme.Meta;
        _onlineStatus.Text = Loc.S(_lang, "正在获取在线索引…", "Fetching online index…");
        try
        {
            var result = await FancyText.Core.StyleRegistry.FetchIndexAsync();
            if (result.Value is null)
            {
                _onlineStatus.Foreground = Brushes.OrangeRed;
                _onlineStatus.Text = Loc.S(_lang, "获取失败，稍后再试：", "Fetch failed, retry later: ") + result.Error;
                return;
            }

            _onlineEntries = result.Value;
            FillOnlineList();
            _onlineStatus.Text = result.Value.Count == 0
                ? Loc.S(_lang, "索引为空", "Index is empty")
                : "";
        }
        catch (Exception ex)
        {
            _onlineStatus.Foreground = Brushes.OrangeRed;
            _onlineStatus.Text = ex.Message; // 兜底：网络层异常不该白屏
        }
        finally
        {
            _onlineBusy = false;
        }
    }

    /// <summary>渲染在线索引列表：已安装的同名包标「已安装」，其余给 [安装]（下载 → 校验导入 → 落盘）。</summary>
    private void FillOnlineList()
    {
        _onlineList.Children.Clear();
        if (_onlineEntries is null)
        {
            return;
        }

        HashSet<string> installedNames;
        try
        {
            installedNames = FancyText.Core.StylePacks.LoadInstalled()
                .Where(p => p.Error is null)
                .Select(p => p.PackName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            installedNames = [];
        }

        foreach (var entry in _onlineEntries)
        {
            var name = new TextBlock
            {
                Text = entry.PackName + (entry.Official ? Loc.S(_lang, " · 官方", " · official") : ""),
                FontSize = 12,
                Foreground = _theme.Text,
            };
            var desc = new TextBlock
            {
                Text = entry.Description,
                FontSize = 10.5,
                Foreground = _theme.Meta,
                TextWrapping = TextWrapping.Wrap,
            };
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(name);
            info.Children.Add(desc);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (installedNames.Contains(entry.PackName))
            {
                right.Children.Add(new TextBlock
                {
                    Text = Loc.S(_lang, "已安装 ✓", "Installed ✓"),
                    FontSize = 11,
                    Foreground = _theme.Meta,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            else
            {
                var installBtn = new Button
                {
                    Content = Loc.S(_lang, "安装", "Install"),
                    FontSize = 11.5,
                    Padding = new Thickness(10, 2, 10, 2),
                    Cursor = Cursors.Hand,
                    Tag = entry,
                };
                StyleButton(installBtn);
                installBtn.Click += (_, _) => OnInstallOnlinePack(entry, installBtn);
                right.Children.Add(installBtn);
            }

            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            row.Children.Add(info);
            _onlineList.Children.Add(row);
        }
    }

    /// <summary>下载并导入一个在线包：下载原文 → 与本地导入同一套校验管线 → 落盘 → 联动刷新。</summary>
    private async void OnInstallOnlinePack(FancyText.Core.RegistryPackEntry entry, Button button)
    {
        if (_onlineBusy)
        {
            return;
        }

        _onlineBusy = true;
        button.IsEnabled = false;
        _onlineStatus.Foreground = _theme.Meta;
        _onlineStatus.Text = Loc.S(_lang, $"正在下载「{entry.PackName}」…", $"Downloading \"{entry.PackName}\"…");
        try
        {
            var download = await FancyText.Core.StyleRegistry.DownloadPackAsync(entry);
            if (download.Value is null)
            {
                _onlineStatus.Foreground = Brushes.OrangeRed;
                _onlineStatus.Text = Loc.S(_lang, "下载失败：", "Download failed: ") + download.Error;
                button.IsEnabled = true;
                return;
            }

            var import = FancyText.Core.StylePacks.ImportJson(download.Value, $"在线:{entry.Id}");
            if (!import.Success)
            {
                _onlineStatus.Foreground = Brushes.OrangeRed;
                _onlineStatus.Text = string.Join("; ", import.Errors);
                button.IsEnabled = true;
                return;
            }

            ApplyPacksChanged(Loc.S(_lang, $"已安装「{entry.PackName}」", $"Installed \"{entry.PackName}\""));
            _onlineStatus.Text = "";
            FillOnlineList(); // 该行换成「已安装 ✓」
        }
        catch (Exception ex)
        {
            _onlineStatus.Foreground = Brushes.OrangeRed;
            _onlineStatus.Text = ex.Message;
            button.IsEnabled = true;
        }
        finally
        {
            _onlineBusy = false;
        }
    }

    /// <summary>重填两组包列表：官方包（exe 内嵌）+ 用户导入包。数据依赖的结构全在这里重建，行展开态按包名跨重填保留。</summary>
    private void FillPacksPanel()
    {
        _packsList.Children.Clear();

        IReadOnlyList<FancyText.Core.InstalledPack> installed;
        IReadOnlyList<FancyText.Core.BundledPack> bundled;
        try
        {
            installed = FancyText.Core.StylePacks.LoadInstalled();
            bundled = FancyText.Core.StylePacks.LoadBundled();
        }
        catch (Exception)
        {
            return; // 目录/内嵌资源不可读等：列表留空，导入按钮的错误提示兜底
        }

        // —— 官方样式包（内嵌资源；无内嵌包时整组隐藏）——
        if (bundled.Count > 0)
        {
            var installedByName = new Dictionary<string, FancyText.Core.InstalledPack>(StringComparer.OrdinalIgnoreCase);
            foreach (var pack in installed)
            {
                installedByName[pack.PackName] = pack;
            }

            _packsList.Children.Add(MakePackGroupHeader(Loc.S(_lang, "官方样式包", "Official packs")));
            foreach (var pack in bundled)
            {
                _packsList.Children.Add(MakeBundledPackRow(pack, installedByName));
            }
        }

        // —— 已安装样式包（用户导入；减去官方同名包，避免两组重复显示）——
        var officialNames = bundled.Select(p => p.PackName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var userPacks = installed.Where(p => !officialNames.Contains(p.PackName)).ToList();

        _packsList.Children.Add(MakePackGroupHeader(Loc.S(_lang, "已安装样式包", "Installed packs")));
        if (userPacks.Count == 0)
        {
            _packsList.Children.Add(new TextBlock
            {
                Text = Loc.S(_lang,
                    $"尚无导入的样式包——把别人分享的 .json 放进 {FancyText.Core.StylePacks.DefaultPacksDirectory} 也行",
                    $"No imported packs yet — dropping a shared .json into {FancyText.Core.StylePacks.DefaultPacksDirectory} also works"),
                FontSize = 10.5,
                Foreground = _theme.Meta,
                Opacity = 0.8,
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var pack in userPacks)
        {
            _packsList.Children.Add(MakeInstalledPackRow(pack));
        }
    }

    /// <summary>面板内的分组小标题（比 MakeGroupHeader 低一档）。</summary>
    private TextBlock MakePackGroupHeader(string text) => new()
    {
        Text = text,
        FontSize = 11.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = _theme.Meta,
        Margin = new Thickness(0, 8, 0, 6),
    };

    /// <summary>官方包行：右侧按钮组按状态切换——未安装 [安装]；已安装未停用 [停用][卸载]；已停用 [启用][卸载]。
    /// 安装状态以包目录里存在同名文件为准（损坏文件按未安装处理，[安装] 覆盖修复）。</summary>
    private FrameworkElement MakeBundledPackRow(FancyText.Core.BundledPack pack,
        IReadOnlyDictionary<string, FancyText.Core.InstalledPack> installedByName)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(buttons, Dock.Right);

        if (installedByName.TryGetValue(pack.PackName, out var installed))
        {
            // 内容漂移检测：磁盘文件与内嵌资源不一致 → 提供 [更新]（覆盖重装）
            if (HasBundledUpdate(pack, installed))
            {
                var updateBtn = MakePackButton(Loc.S(_lang, "更新", "Update"));
                updateBtn.Click += (_, _) =>
                {
                    var result = FancyText.Core.StylePacks.InstallBundled(pack);
                    if (!result.Success)
                    {
                        ShowPackError(Loc.S(_lang, $"更新失败：{string.Join("\n", result.Errors)}", $"Update failed: {string.Join("\n", result.Errors)}"));
                        return;
                    }

                    _main.Usage.SetPackDisabled(pack.PackName, false);
                    ApplyPacksChanged(Loc.S(_lang, "已更新 ✓", "Updated ✓"));
                };
                buttons.Children.Add(updateBtn);
            }

            AddPackActionButtons(buttons, pack.PackName, _main.Usage.IsPackDisabled(pack.PackName), installed);
        }
        else
        {
            var installBtn = MakePackButton(Loc.S(_lang, "安装", "Install"));
            installBtn.Click += (_, _) =>
            {
                var result = FancyText.Core.StylePacks.InstallBundled(pack);
                if (!result.Success)
                {
                    ShowPackError(Loc.S(_lang, $"安装失败：{string.Join("\n", result.Errors)}", $"Install failed: {string.Join("\n", result.Errors)}"));
                    return;
                }

                // 安装即启用：清掉同名包可能残留的停用标记，否则"装了却看不到样式"
                _main.Usage.SetPackDisabled(pack.PackName, false);
                ApplyPacksChanged(Loc.S(_lang, "已安装 ✓（命令面板插件需重启后生效）", "Installed ✓ (Command Palette extension picks it up after restart)"));
            };
            buttons.Children.Add(installBtn);
        }

        // 已安装时以磁盘实际内容为准展开预览（防资源/文件漂移），未安装预览内嵌资源
        return MakePackRow(pack.PackName, installed?.Styles ?? pack.Styles, buttons);
    }

    /// <summary>官方包磁盘内容与内嵌资源是否不一致（不一致 = 有更新可装）。</summary>
    private static bool HasBundledUpdate(FancyText.Core.BundledPack pack, FancyText.Core.InstalledPack installed)
    {
        try
        {
            return !string.Equals(File.ReadAllText(installed.FilePath), pack.Json, StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false; // 读不到文件按无更新（行内卸载后重装仍是兜底路径）
        }
    }

    /// <summary>用户导入包行：健康包给启停 + 卸载 + 展开预览；损坏包保持标红，不给启停（只能卸载）。</summary>
    private FrameworkElement MakeInstalledPackRow(FancyText.Core.InstalledPack pack)
    {
        if (pack.Error is not null)
        {
            var text = new TextBlock
            {
                Text = Loc.S(_lang, $"{Path.GetFileName(pack.FilePath)}（损坏：{pack.Error}）", $"{Path.GetFileName(pack.FilePath)} (broken: {pack.Error})"),
                FontSize = 11.5,
                Foreground = Frozen(0xD1, 0x3B, 0x3B),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var removeBtn = MakePackButton(Loc.S(_lang, "卸载", "Remove"));
            removeBtn.Click += (_, _) =>
            {
                if (FancyText.Core.StylePacks.Remove(pack))
                {
                    _main.Usage.SetPackDisabled(pack.PackName, false); // 同步清停用标记（损坏包也可能是曾停用的好包）
                    ApplyPacksChanged(Loc.S(_lang, $"已卸载 {pack.PackName}", $"Removed {pack.PackName}"));
                }
            };
            DockPanel.SetDock(removeBtn, Dock.Right);

            var brokenRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            brokenRow.Children.Add(removeBtn);
            brokenRow.Children.Add(text);
            return brokenRow;
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(buttons, Dock.Right);
        AddPackActionButtons(buttons, pack.PackName, _main.Usage.IsPackDisabled(pack.PackName), pack);
        return MakePackRow(pack.PackName, pack.Styles, buttons);
    }

    /// <summary>启停 + 卸载按钮（官方已装包与用户健康包共用）：停用只是从列表隐藏样式、文件保留，卸载才删文件。</summary>
    private void AddPackActionButtons(StackPanel host, string packName, bool disabled, FancyText.Core.InstalledPack installed)
    {
        var toggleBtn = MakePackButton(disabled ? Loc.S(_lang, "启用", "Enable") : Loc.S(_lang, "停用", "Disable"));
        toggleBtn.Click += (_, _) =>
        {
            _main.Usage.SetPackDisabled(packName, !disabled);
            ApplyPacksChanged(disabled
                ? Loc.S(_lang, $"已启用 {packName}", $"Enabled {packName}")
                : Loc.S(_lang, $"已停用 {packName}（文件保留，随时可启用）", $"Disabled {packName} (file kept; re-enable anytime)"));
        };
        host.Children.Add(toggleBtn);

        var removeBtn = MakePackButton(Loc.S(_lang, "卸载", "Remove"));
        removeBtn.Margin = new Thickness(6, 0, 0, 0);
        removeBtn.Click += (_, _) =>
        {
            if (FancyText.Core.StylePacks.Remove(installed))
            {
                _main.Usage.SetPackDisabled(packName, false); // 卸载顺手清停用标记，防孤儿条目（同名包日后再装不应背着停用态）
                ApplyPacksChanged(Loc.S(_lang, $"已卸载 {packName}", $"Removed {packName}"));
            }
        };
        host.Children.Add(removeBtn);
    }

    /// <summary>包行右侧小按钮：统一主题样式（与面板顶部导入/导出同款）。</summary>
    private Button MakePackButton(string text)
    {
        var button = new Button
        {
            Content = text,
            FontSize = 11,
            Padding = new Thickness(8, 2, 8, 2),
            Cursor = Cursors.Hand,
        };
        StyleButton(button);
        return button;
    }

    /// <summary>健康包行骨架（官方/用户共用）：左侧小展开钮（▸/▾）+ 包名（N 个样式），右侧按钮组；
    /// 展开后在行下逐样式列出「样式名 + 当前输入的转换预览」。高度变化内联完成，不做动画。</summary>
    private FrameworkElement MakePackRow(string packName, IReadOnlyList<TextStyle> styles, StackPanel buttons)
    {
        var expanded = _expandedPacks.Contains(packName);

        var expandBtn = new Button
        {
            Content = expanded ? "▾" : "▸",
            FontSize = 10,
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = Loc.S(_lang, "展开预览包内样式", "Preview styles in this pack"),
        };
        StyleButton(expandBtn);
        DockPanel.SetDock(expandBtn, Dock.Left);

        var text = new TextBlock
        {
            Text = Loc.S(_lang, $"{packName}（{styles.Count} 个样式）", $"{packName} ({styles.Count} styles)"),
            FontSize = 11.5,
            Foreground = _theme.Text,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        row.Children.Add(buttons);   // 右侧按钮组
        row.Children.Add(expandBtn); // 左侧展开钮
        row.Children.Add(text);      // 填充余下空间（TextTrimming 需要受限宽度）

        var host = new StackPanel();
        host.Children.Add(row);
        if (expanded)
        {
            host.Children.Add(MakePackStylesDetail(styles));
        }

        expandBtn.Click += (_, _) =>
        {
            if (!_expandedPacks.Remove(packName))
            {
                _expandedPacks.Add(packName);
            }

            FillPacksPanel(); // 只重填列表区域，状态行文本保留
        };

        return host;
    }

    /// <summary>展开的包内样式列表：样式名 + 用主窗当前输入（前 64 字素，与主窗预览同一约定）做的单行转换预览。
    /// 预览用普通 TextBlock + 主窗同款字体回退链（PreviewTextBlock 是主窗私有类，这里不复用）；
    /// 转换异常/空结果显示灰色「（不适用）」。</summary>
    private StackPanel MakePackStylesDetail(IReadOnlyList<TextStyle> styles)
    {
        var panel = new StackPanel { Margin = new Thickness(26, 0, 0, 8) };

        var input = _main.CurrentInput;
        if (string.IsNullOrWhiteSpace(input))
        {
            input = FancyText.Core.StyleCatalog.DefaultSample; // 主窗尚无输入时给示例，避免整列「不适用」（同主窗首开行为）
        }

        var previewInput = TextElementTruncator.Truncate(input, 64);

        foreach (var style in styles)
        {
            var name = new TextBlock
            {
                Text = style.GetName(_lang),
                FontSize = 11,
                Foreground = _theme.Text,
                Opacity = 0.85,
                MaxWidth = 130,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            DockPanel.SetDock(name, Dock.Left);

            string? preview = null;
            try
            {
                preview = style.Transform(previewInput);
            }
            catch (Exception)
            {
                // 单样式失败按「不适用」处理（同主窗列表的容错粒度）
            }

            var applicable = !string.IsNullOrEmpty(preview);
            var previewText = new TextBlock
            {
                Text = applicable ? OneLine(preview!) : Loc.S(_lang, "（不适用）", "(n/a)"),
                FontSize = 11.5,
                FontFamily = new FontFamily(MainWindow.PreviewFontChain),
                Foreground = _theme.Meta,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
            line.Children.Add(name);
            line.Children.Add(previewText); // 填充余下宽度
            panel.Children.Add(line);
        }

        return panel;
    }

    /// <summary>预览单行化并限长（与主窗 OneLine 同款：160 码元截断，防 Zalgo 之类撑爆测量）。</summary>
    private static string OneLine(string text)
    {
        var single = text.ReplaceLineEndings(" ");
        return single.Length <= 160 ? single : single[..160] + "…";
    }

    private void OnImportPack()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.S(_lang, "导入样式包", "Import style pack"),
            Filter = Loc.S(_lang, "样式包 (*.json)|*.json|所有文件 (*.*)|*.*", "Style pack (*.json)|*.json|All files (*.*)|*.*"),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var result = FancyText.Core.StylePacks.Import(dialog.FileName);
        if (!result.Success)
        {
            ShowPackError(Loc.S(_lang, $"导入失败：{string.Join("\n", result.Errors)}", $"Import failed: {string.Join("\n", result.Errors)}"));
            return;
        }

        // 导入即启用：同名包若曾停用（卸载残留标记/手动停用后重装），必须清标记否则"装了却看不到样式"
        var importedName = FancyText.Core.StylePacks.LoadInstalled()
            .FirstOrDefault(p => string.Equals(p.FilePath, result.InstalledPath, StringComparison.OrdinalIgnoreCase))?.PackName;
        if (importedName is not null)
        {
            _main.Usage.SetPackDisabled(importedName, false);
        }

        ApplyPacksChanged(Loc.S(_lang, "已导入 ✓（命令面板插件需重启后生效）", "Imported ✓ (Command Palette extension picks it up after restart)"));
    }

    private void OnExportPinnedPack()
    {
        var pinned = _main.PinnedStyles;
        if (pinned.Count == 0)
        {
            ShowPackError(Loc.S(_lang, "还没有收藏任何样式——在列表里按 Ctrl+D 收藏后再导出", "Nothing pinned yet — press Ctrl+D in the list to pin styles first"));
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Loc.S(_lang, "导出收藏为样式包", "Export pinned as style pack"),
            Filter = Loc.S(_lang, "样式包 (*.json)|*.json", "Style pack (*.json)|*.json"),
            FileName = Loc.S(_lang, "我的收藏.json", "My-pinned.json"),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var packName = Path.GetFileNameWithoutExtension(dialog.FileName);
            var pack = FancyText.Core.StylePacks.ExportStyles(pinned, packName, author: null);
            File.WriteAllText(dialog.FileName, FancyText.Core.StylePacks.Serialize(pack));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowPackError(Loc.S(_lang, $"导出失败：{ex.Message}", $"Export failed: {ex.Message}"));
            return;
        }

        _packStatus.Foreground = _theme.Primary;
        _packStatus.Text = Loc.S(_lang,
            $"已导出 {pinned.Count} 个收藏样式 ✓ 发送这个文件即可分享",
            $"Exported {pinned.Count} pinned styles ✓ share this file");
    }

    /// <summary>包目录/启停状态变动后的统一收尾：重载目录 + 刷新主窗口 + 重填面板列表。</summary>
    private void ApplyPacksChanged(string message)
    {
        FancyText.Core.StyleCatalog.Reload();
        _main.RefreshStyles();
        FillPacksPanel();
        _packStatus.Foreground = _theme.Primary;
        _packStatus.Text = message;
    }

    private void ShowPackError(string message)
    {
        _packStatus.Foreground = Frozen(0xD1, 0x3B, 0x3B);
        _packStatus.Text = message.ReplaceLineEndings("  ");
    }

    // ================================================== 交互 ==================================================

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recordingHotkey)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            EndRecording();
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
        {
            return; // 修饰键按下属于组合过程，不结束录制
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
        {
            _hotkeyHint.Foreground = Frozen(0xD1, 0x3B, 0x3B);
            _hotkeyHint.Text = Loc.S(_lang, "需包含 Ctrl / Alt / Shift / Win 中至少一个", "Include at least one of Ctrl / Alt / Shift / Win");
            return;
        }

        var display = $"{ModifiersText(modifiers)}{key}";
        if (DesktopSettings.ParseHotkey(display) is not { } binding)
        {
            _hotkeyHint.Foreground = Frozen(0xD1, 0x3B, 0x3B);
            _hotkeyHint.Text = Loc.S(_lang, "该组合不可用，换一个试试", "That combo is unavailable, try another");
            return;
        }

        if (_main.TryRebindHotkey(binding))
        {
            Save(_settings with { Hotkey = display });
            _recordingHotkey = false;
            _hotkeyPill.Content = display;
            _hotkeyPill.FontWeight = FontWeights.Normal;
            _hotkeyHint.Foreground = _theme.Primary;
            _hotkeyHint.Text = Loc.S(_lang, "已生效", "Applied");
        }
        else
        {
            _hotkeyHint.Foreground = Frozen(0xD1, 0x3B, 0x3B);
            _hotkeyHint.Text = Loc.S(_lang,
                $"组合键已被其他程序占用，保留原热键 {_main.HotkeyDisplay}",
                $"Combo already taken by another app; keeping {_main.HotkeyDisplay}");
            _hotkeyPill.Content = _main.HotkeyDisplay; // 录制态文字复原
            _hotkeyPill.FontWeight = FontWeights.Normal;
            _recordingHotkey = false;
        }
    }

    private void EndRecording()
    {
        _recordingHotkey = false;
        _hotkeyPill.Content = _main.HotkeyDisplay;
        _hotkeyPill.FontWeight = FontWeights.Normal;
        _hotkeyHint.Text = "";
    }

    private void ApplyLaunchAtLogin(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(AutoRunKey);
            if (enable)
            {
                key?.SetValue(AutoRunName, $"\"{Environment.ProcessPath}\"");
            }
            else
            {
                key?.DeleteValue(AutoRunName, throwOnMissingValue: false);
            }

            Save(_settings with { LaunchAtLogin = enable });
        }
        catch (Exception)
        {
            // 注册表被策略禁用等：设置不保存，开关下次打开仍为旧值
        }
    }

    /// <summary>保存 + 应用到主窗口；retheme 时主弹窗与设置窗一起换肤。</summary>
    private void Save(DesktopSettings settings, bool retheme = false)
    {
        _settings = settings;
        _settings.Save();
        _main.ApplySettings(settings);
        if (retheme)
        {
            _theme = _main.CurrentTheme;
            // 重建在事件处理器内替换自身控件树有重入风险，延迟到空闲
            Dispatcher.BeginInvoke(BuildContent);
            ApplyWindowChrome(); // 标题栏明暗随主题
        }
    }

    private static string ModifiersText(ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        return string.Join("+", parts) + "+";
    }

    private static Color ParseColor(string hex) =>
        DesktopSettings.TryParseColor(hex, out var r, out var g, out var b) ? Color.FromRgb(r, g, b) : Colors.Purple;

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
