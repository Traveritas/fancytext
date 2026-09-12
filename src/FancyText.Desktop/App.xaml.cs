using System.Windows;
using FancyText.Core;

using WinForms = System.Windows.Forms;

namespace FancyText.Desktop;

/// <summary>
/// 桌面版入口：启动即无主窗口常驻——只留托盘图标 + 全局热键，热键/托盘/二次启动唤出弹窗。
/// 单实例：Mutex 防多开，二次启动通过命名事件唤醒已有实例弹窗后自行退出。
/// </summary>
public partial class App : Application, IDisposable
{
    private const string MutexName = @"Local\FancyText.Desktop.SingleInstance";
    private const string ActivateEventName = @"Local\FancyText.Desktop.Activate";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activateSignal;
    private RegisteredWaitHandle? _activateRegistration;
    private MainWindow? _mainWindow;
    private TrayIconController? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 常驻托盘工具：未处理异常只丢掉当次操作，不应把整个进程带走
        DispatcherUnhandledException += (_, args) => args.Handled = true;

        // WinForms 仅用于托盘菜单，开启视觉样式让 ContextMenuStrip 质感与系统一致
        WinForms.Application.EnableVisualStyles();

        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            // 二次启动：唤醒已有实例弹出窗口，自己立即退出
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ActivateEventName);
                signal.Set();
            }
            catch (Exception)
            {
                // 已有实例可能正在退出，唤醒失败也无妨
            }

            Shutdown();
            return;
        }

        // 与 CmdPal 插件共享 %LOCALAPPDATA%\FancyText\state.json（收藏/最近）
        var usage = new UsageState();
        _mainWindow = new MainWindow(usage);

        // 已有实例被二次启动唤醒：线程池回调必须转回 UI 线程才能操作窗口
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _activateRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activateSignal,
            callBack: (_, _) => Dispatcher.BeginInvoke(() => _mainWindow?.ShowPopup()),
            state: null,
            millisecondsTimeOutInterval: -1,
            executeOnlyOnce: false);

        _tray = new TrayIconController(_mainWindow);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    /// <summary>退出清理。重复调用安全（字段逐个置空）。</summary>
    public void Dispose()
    {
        _tray?.Dispose(); // 先摘托盘图标，避免残留"幽灵图标"
        _tray = null;

        _activateRegistration?.Unregister(null);
        _activateRegistration = null;
        _activateSignal?.Dispose();
        _activateSignal = null;

        try
        {
            _mainWindow?.Close(); // 触发 OnClosed：注销热键、退订事件（重复 Close 无害）
        }
        catch (Exception)
        {
            // 退出兜底：清理异常不阻止进程结束
        }

        _mainWindow = null;
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;

        GC.SuppressFinalize(this);
    }
}
