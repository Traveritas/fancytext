using Shmuelie.WinRTServer.CsWinRT;

namespace FancyText.CmdPal;

// 入口：Command Palette 通过 COM 激活本进程（-RegisterProcessAsComServer），
// 直接双击运行时无事可做（扩展由 CmdPal 托管）。
public static class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            using var extensionDisposedEvent = new ManualResetEvent(false);

            var server = new Shmuelie.WinRTServer.ComServer();
            var extensionInstance = new FancyTextExtension(extensionDisposedEvent);
            server.RegisterClass<FancyTextExtension, Microsoft.CommandPalette.Extensions.IExtension>(() => extensionInstance);
            server.Start();

            WaitHandle.WaitAny([extensionDisposedEvent]);
            server.UnsafeDispose();
        }
    }
}
