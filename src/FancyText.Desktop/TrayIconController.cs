using System.Drawing;
using System.Drawing.Drawing2D;
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
    private IntPtr _iconHandle; // bitmap.GetHicon() 的句柄不归 Icon 管，退出时自毁

    public TrayIconController(MainWindow window, Action openSettings)
    {
        _window = window;
        _icon = CreateIcon();
        _iconHandle = _icon.Handle;

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
        UpdateTooltip();
    }

    private void UpdateTooltip() => _notifyIcon.Text =
        Loc.S(_window.Lang, $"花式文字 · {_window.HotkeyDisplay} 唤出", $"Fancy Text · {_window.HotkeyDisplay} to open");

    /// <summary>图标用代码画：主色方块 + 白色「花」字，免去资源文件。</summary>
    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var background = new SolidBrush(Color.FromArgb(0x63, 0x52, 0xDC));
            g.FillRectangle(background, 2, 2, 28, 28);

            using var font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
            var size = g.MeasureString("花", font);
            using var foreground = new SolidBrush(Color.White);
            g.DrawString("花", font, foreground, (32 - size.Width) / 2f, (32 - size.Height) / 2f);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        // 先 Visible=false 再 Dispose，托盘区才不会残留灰图标
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        if (_iconHandle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
    }
}
