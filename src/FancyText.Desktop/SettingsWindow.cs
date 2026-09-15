using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        BuildContent();
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
        root.Children.Add(MakeRow(Loc.S(lang, "强调色", "Accent color"), MakeAccentPanel()));
        root.Children.Add(MakeRow(Loc.S(lang, "预览字号", "Preview size"), MakePreviewSizeCombo()));

        root.Children.Add(MakeSeparator());

        // —— 快捷键 ——
        root.Children.Add(MakeGroupHeader(Loc.S(lang, "快捷键", "Hotkey")));
        root.Children.Add(MakeHotkeyRow());

        root.Children.Add(MakeSeparator());

        // —— 行为 ——
        root.Children.Add(MakeGroupHeader(Loc.S(lang, "行为", "Behavior")));
        root.Children.Add(MakeRow(Loc.S(lang, "开机自启动", "Launch at login"),
            MakeToggle(nameof(_settings.LaunchAtLogin), _settings.LaunchAtLogin, ApplyLaunchAtLogin)));
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

    /// <summary>下拉框深浅色适配：WPF 默认模板对 Background 生效有限，统一走浅色胶囊底座保证可读。</summary>
    private void StyleCombo(ComboBox combo)
    {
        combo.Foreground = _theme.Text;
        combo.Background = _theme.KeycapBackground;
        combo.Margin = new Thickness(0);
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

    private CheckBox MakeToggle(string name, bool value, Action<bool> apply)
    {
        var box = new CheckBox
        {
            IsChecked = value,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = name,
        };
        box.Checked += (_, _) => apply(true);
        box.Unchecked += (_, _) => apply(false);
        return box;
    }

    /// <summary>统一小按钮：主题底色/文字色（默认白按钮在深色主题下过亮）。</summary>
    private void StyleButton(Button button)
    {
        button.Foreground = _theme.KeycapText;
        button.Background = _theme.KeycapBackground;
        button.BorderBrush = _theme.KeycapBorder;
        button.BorderThickness = new Thickness(1);
    }

    /// <summary>强调色面板：8 个预设圆点 + 自定义十六进制输入。</summary>
    private StackPanel MakeAccentPanel()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var swatches = new StackPanel { Orientation = Orientation.Horizontal };
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
                BorderBrush = string.Equals(_settings.Accent, hexCapture, StringComparison.OrdinalIgnoreCase)
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
            Text = _settings.Accent,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var hexHint = new TextBlock
        {
            Text = "",
            FontSize = 10.5,
            Foreground = _theme.Meta,
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
    /// 样式包区块：导入（.json 包文件 → %LOCALAPPDATA%\FancyText\styles）、导出收藏为包、
    /// 已安装包列表（含卸载）。导入/卸载后即时刷新主窗口列表；命令面板插件需重启其进程后生效。
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
        FillInstalledPacks();

        var panel = new StackPanel();
        buttons.Children.Add(importBtn);
        buttons.Children.Add(exportBtn);
        panel.Children.Add(buttons);
        panel.Children.Add(_packStatus);
        panel.Children.Add(_packsList);
        return panel;
    }

    private void FillInstalledPacks()
    {
        _packsList.Children.Clear();
        IReadOnlyList<FancyText.Core.InstalledPack> packs;
        try
        {
            packs = FancyText.Core.StylePacks.LoadInstalled();
        }
        catch (Exception)
        {
            return; // 目录不可读等：列表留空，导入按钮的错误提示兜底
        }

        if (packs.Count == 0)
        {
            _packsList.Children.Add(new TextBlock
            {
                Text = Loc.S(_lang,
                    $"尚未安装样式包——把别人分享的 .json 放进 {FancyText.Core.StylePacks.DefaultPacksDirectory} 也行",
                    $"No style packs installed — dropping a shared .json into {FancyText.Core.StylePacks.DefaultPacksDirectory} also works"),
                FontSize = 10.5,
                Foreground = _theme.Meta,
                Opacity = 0.8,
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var pack in packs)
        {
            var label = pack.Error is null
                ? Loc.S(_lang, $"{pack.PackName}（{pack.Styles.Count} 个样式）", $"{pack.PackName} ({pack.Styles.Count} styles)")
                : Loc.S(_lang, $"{Path.GetFileName(pack.FilePath)}（损坏：{pack.Error}）", $"{Path.GetFileName(pack.FilePath)} (broken: {pack.Error})");
            var text = new TextBlock
            {
                Text = label,
                FontSize = 11.5,
                Foreground = pack.Error is null ? _theme.Text : Frozen(0xD1, 0x3B, 0x3B),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            DockPanel.SetDock(text, Dock.Left);

            var removeBtn = new Button
            {
                Content = Loc.S(_lang, "卸载", "Remove"),
                FontSize = 11,
                Padding = new Thickness(8, 2, 8, 2),
                Cursor = Cursors.Hand,
            };
            StyleButton(removeBtn);
            removeBtn.Click += (_, _) =>
            {
                if (FancyText.Core.StylePacks.Remove(pack))
                {
                    ApplyPacksChanged(Loc.S(_lang, $"已卸载 {pack.PackName}", $"Removed {pack.PackName}"));
                }
            };
            DockPanel.SetDock(removeBtn, Dock.Right);

            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(removeBtn);
            row.Children.Add(text);
            _packsList.Children.Add(row);
        }
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

    /// <summary>包目录变动后的统一收尾：重载目录 + 刷新主窗口 + 重画已装包列表。</summary>
    private void ApplyPacksChanged(string message)
    {
        FancyText.Core.StyleCatalog.Reload();
        _main.RefreshStyles();
        FillInstalledPacks();
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
