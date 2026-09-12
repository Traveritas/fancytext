using System.Drawing;
using System.Drawing.Drawing2D;
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

        var openItem = new WinForms.ToolStripMenuItem("打开(&O)");
        openItem.Click += (_, _) => _window.ShowPopup();

        var settingsItem = new WinForms.ToolStripMenuItem("设置(&S)…");
        settingsItem.Click += (_, _) => openSettings();

        var exitItem = new WinForms.ToolStripMenuItem("退出(&X)");
        exitItem.Click += (_, _) => System.Windows.Application.Current.Shutdown();

        _menu.Items.Add(openItem);
        _menu.Items.Add(settingsItem);
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add(exitItem);
        _menu.Opening += (_, _) => UpdateTooltip(); // 热键换绑后提示文字随下次打开刷新

        _notifyIcon.Icon = _icon;
        _notifyIcon.Text = "花式文字";
        _notifyIcon.ContextMenuStrip = _menu;
        _notifyIcon.Visible = true;
        _notifyIcon.DoubleClick += (_, _) => _window.ShowPopup();
        UpdateTooltip();
    }

    /// <summary>tooltip 有 63 字符上限，超长会被截断，这里固定短格式。</summary>
    private void UpdateTooltip() => _notifyIcon.Text = $"花式文字 · {_window.HotkeyDisplay} 唤出";

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
