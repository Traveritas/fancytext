using System.Drawing;
using FancyText.Core;
using FancyText.Desktop.Helpers;

using WinForms = System.Windows.Forms;

namespace FancyText.Desktop;

/// <summary>
/// 托盘常驻：NotifyIcon + 右键菜单（打开 / 设置 / 退出），双击唤出弹窗。
/// WPF Dispatcher 本身就是本线程的 Win32 消息泵，NotifyIcon 在 UI 线程创建即可正常收消息。
/// </summary>
internal sealed class TrayIconController : IDisposable
{
    private readonly MainWindow _window;
    private readonly WinForms.NotifyIcon _notifyIcon = new();
    private readonly WinForms.ContextMenuStrip _menu = new();
    private readonly Icon _icon;

    public TrayIconController(MainWindow window, Action openSettings)
    {
        _window = window;
        _icon = CreateIcon();

        var lang = _window.Lang;
        var openItem = new WinForms.ToolStripMenuItem(Loc.S(lang, "打开(&O)", "&Open"));
        openItem.Click += (_, _) => _window.ShowPopup();

        var settingsItem = new WinForms.ToolStripMenuItem(Loc.S(lang, "设置(&S)…", "&Settings…"));
        settingsItem.Click += (_, _) => openSettings();

        var exitItem = new WinForms.ToolStripMenuItem(Loc.S(lang, "退出(&X)", "E&xit"));
        exitItem.Click += (_, _) => System.Windows.Application.Current.Shutdown();

        _menu.Items.Add(openItem);
        _menu.Items.Add(settingsItem);
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add(exitItem);
        // 主题化渲染：Win11 已为这类菜单画好圆角，这里只把配色接到主窗主题（深浅色即时跟随）
        _menu.Renderer = new WinForms.ToolStripProfessionalRenderer(new ThemedColorTable(_window));
        _menu.Font = new Font("Microsoft YaHei UI", 9.5f);
        // 打开菜单时按当前语言刷新全部文案（热键换绑/语言切换后随下次打开生效，零重建链路）
        _menu.Opening += (_, _) => RefreshTexts();

        _notifyIcon.Icon = _icon;
        _notifyIcon.Text = AppName();
        _notifyIcon.ContextMenuStrip = _menu;
        _notifyIcon.Visible = true;
        _notifyIcon.DoubleClick += (_, _) => _window.ShowPopup();
        UpdateTooltip();
    }

    private static string AppName() => "FancyText";

    /// <summary>菜单与 tooltip 按当前语言刷新（tooltip 有 63 字符上限，固定短格式）。</summary>
    private void RefreshTexts()
    {
        var lang = _window.Lang;
        _menu.Items[0].Text = Loc.S(lang, "打开(&O)", "&Open");
        _menu.Items[1].Text = Loc.S(lang, "设置(&S)…", "&Settings…");
        _menu.Items[3].Text = Loc.S(lang, "退出(&X)", "E&xit");
        // 文字色跟随主题（配色表只管背景/高亮/边框，项文字色走 ForeColor）
        var fg = ((System.Windows.Media.SolidColorBrush)_window.CurrentTheme.Text).Color;
        var textColor = Color.FromArgb(fg.A, fg.R, fg.G, fg.B);
        foreach (WinForms.ToolStripItem item in _menu.Items)
        {
            item.ForeColor = textColor;
        }

        UpdateTooltip();
    }

    /// <summary>托盘菜单配色表：每次绘制时从主窗当前主题取色（主题切换即时生效，无需重建菜单）。
    /// 只管配色——圆角/阴影由 Win11 系统菜单窗口自带，行为保持 WinForms 原生。</summary>
    private sealed class ThemedColorTable : WinForms.ProfessionalColorTable
    {
        private readonly MainWindow _main;

        public ThemedColorTable(MainWindow main) => _main = main;

        private Theme T => _main.CurrentTheme;

        private static Color C(System.Windows.Media.Brush brush) =>
            brush is System.Windows.Media.SolidColorBrush solid
                ? Color.FromArgb(solid.Color.A, solid.Color.R, solid.Color.G, solid.Color.B)
                : Color.Magenta;

        public override Color ToolStripDropDownBackground => C(T.FlyoutBackground);
        public override Color MenuBorder => C(T.WindowBorder);
        public override Color ImageMarginGradientBegin => C(T.FlyoutBackground);
        public override Color ImageMarginGradientMiddle => C(T.FlyoutBackground);
        public override Color ImageMarginGradientEnd => C(T.FlyoutBackground);
        public override Color MenuItemSelected => C(T.HoverItem);
        public override Color MenuItemSelectedGradientBegin => C(T.HoverItem);
        public override Color MenuItemSelectedGradientEnd => C(T.HoverItem);
        public override Color MenuItemPressedGradientBegin => C(T.SelectedItem);
        public override Color MenuItemPressedGradientMiddle => C(T.SelectedItem);
        public override Color MenuItemPressedGradientEnd => C(T.SelectedItem);
        public override Color SeparatorDark => C(T.Separator);
        public override Color SeparatorLight => C(T.Separator);
        public override Color MenuItemBorder => C(T.Primary);
        public override Color CheckBackground => C(T.HoverItem);
        public override Color CheckSelectedBackground => C(T.HoverItem);
        public override Color CheckPressedBackground => C(T.SelectedItem);
    }

    private void UpdateTooltip() => _notifyIcon.Text =
        Loc.S(_window.Lang, $"花式文字 · {_window.HotkeyDisplay} 唤出", $"Fancy Text · {_window.HotkeyDisplay} to open");

    /// <summary>托盘图标：取 exe 自身的多尺寸图标（csproj ApplicationIcon 嵌入的 app.ico），取 16px 帧。</summary>
    private static Icon CreateIcon()
    {
        using var extracted = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        return new Icon(extracted, 16, 16); // 多尺寸 ico 里挑 16px 帧；Icon 自持句柄，Dispose 即回收
    }

    public void Dispose()
    {
        // 先 Visible=false 再 Dispose，托盘区才不会残留灰图标
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose(); // ExtractAssociatedIcon 得来的 Icon 自持句柄
    }
}
