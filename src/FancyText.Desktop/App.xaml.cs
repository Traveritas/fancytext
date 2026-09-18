using System.IO;
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
    private const string SettingsEventName = @"Local\FancyText.Desktop.Settings";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activateSignal;
    private RegisteredWaitHandle? _activateRegistration;
    private EventWaitHandle? _settingsSignal;
    private RegisteredWaitHandle? _settingsRegistration;
    private MainWindow? _mainWindow;
    private TrayIconController? _tray;
    private SettingsWindow? _settingsWindow;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 常驻托盘工具：未处理异常只丢掉当次操作，不应把整个进程带走。
        // 但必须留下证据（写诊断日志），否则启动路径上的异常会让进程变成"无窗口无托盘"的空壳且无从排查。
        DispatcherUnhandledException += (_, args) =>
        {
            LogDiag($"未处理异常: {args.Exception}", always: true); // 兜底证据不受日志开关影响
            args.Handled = true;
        };
        LogDiag("startup: OnStartup 开始");

        // WinForms 仅用于托盘菜单，开启视觉样式让 ContextMenuStrip 质感与系统一致
        WinForms.Application.EnableVisualStyles();

        var openSettings = e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase);
        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        _ownsMutex = createdNew; // 二次实例从未拥有互斥体，退出时不可 Release
        if (!createdNew)
        {
            // 二次启动：唤醒已有实例（弹窗或设置窗口，取决于参数），自己立即退出
            try
            {
                using var signal = EventWaitHandle.OpenExisting(openSettings ? SettingsEventName : ActivateEventName);
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

        // 后台预热字体覆盖表（约 200ms）：组合符动态解析就绪，首次唤出零等待
        System.Threading.Tasks.Task.Run(() => Helpers.FontCoverage.Warmup());

        // 已有实例被二次启动唤醒：线程池回调必须转回 UI 线程才能操作窗口
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _activateRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activateSignal,
            callBack: (_, _) => Dispatcher.BeginInvoke(() => _mainWindow?.ShowPopup()),
            state: null,
            millisecondsTimeOutInterval: -1,
            executeOnlyOnce: false);

        // --settings 二次启动：打开设置窗口（快捷方式/自动化测试入口）
        _settingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, SettingsEventName);
        _settingsRegistration = ThreadPool.RegisterWaitForSingleObject(
            _settingsSignal,
            callBack: (_, _) => Dispatcher.BeginInvoke(OpenSettings),
            state: null,
            millisecondsTimeOutInterval: -1,
            executeOnlyOnce: false);

        _tray = new TrayIconController(_mainWindow, OpenSettings);
    }

    /// <summary>打开设置窗口：单例（已开则提前），随关随清引用。</summary>
    private void OpenSettings()
    {
        if (_settingsWindow is { } open)
        {
            open.Activate();
            return;
        }

        if (_mainWindow is null)
        {
            return;
        }

        _settingsWindow = new SettingsWindow(_mainWindow);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    /// <summary>诊断日志开关：默认关闭（每热键同步写盘拖慢唤出且无限增长），设环境变量 FANCYTEXT_DIAG=1 开启。</summary>
    internal static readonly bool DiagEnabled =
        Environment.GetEnvironmentVariable("FANCYTEXT_DIAG") is "1" or "true";

    /// <summary>诊断日志（%LOCALAPPDATA%\FancyText\diag.log）。默认关闭，always=true 无条件写
    /// （未处理异常等兜底证据必须落盘，不受开关影响）。</summary>
    internal static void LogDiag(string line, bool always = false)
    {
        if (!always && !DiagEnabled)
        {
            return;
        }

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

    /// <summary>退出清理。重复调用安全（字段逐个置空）。</summary>
    public void Dispose()
    {
        _tray?.Dispose(); // 先摘托盘图标，避免残留"幽灵图标"
        _tray = null;

        _activateRegistration?.Unregister(null);
        _activateRegistration = null;
        _activateSignal?.Dispose();
        _activateSignal = null;

        _settingsRegistration?.Unregister(null);
        _settingsRegistration = null;
        _settingsSignal?.Dispose();
        _settingsSignal = null;

        try
        {
            _mainWindow?.Close(); // 触发 OnClosed：注销热键、退订事件（重复 Close 无害）
        }
        catch (Exception)
        {
            // 退出兜底：清理异常不阻止进程结束
        }

        _mainWindow = null;
        if (_ownsMutex)
        {
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 极端时序下已被释放，忽略
            }
        }

        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;

        GC.SuppressFinalize(this);
    }
}
