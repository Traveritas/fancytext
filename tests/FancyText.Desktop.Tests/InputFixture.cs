using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace FancyText.Desktop.Tests;

internal sealed record FixtureRequest(string Action, string Field = "first", string Text = "", int Start = 0, int Length = 0);
internal sealed record FixtureState(long Window, string Text, string OtherText);

/// <summary>独立进程的真实 WPF 文本框；只接受本轮测试的随机命名管道，不接触用户应用。</summary>
internal static class InputFixture
{
    public static int Run(string pipeName)
    {
        var app = new Application();
        var first = new TextBox { Text = "fixture", AcceptsReturn = true, Height = 65 };
        var second = new TextBox { Text = "other", Height = 36 };
        var password = new PasswordBox { Height = 36 };
        var button = new Button { Content = "Not a text field", Height = 36 };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(first);
        panel.Children.Add(second);
        panel.Children.Add(password);
        panel.Children.Add(button);
        var window = new Window { Title = "FancyText isolated input fixture", Width = 480, Height = 320, Content = panel };
        window.Loaded += async (_, _) =>
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(5000);
            using var reader = new StreamReader(pipe);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(new FixtureState(new WindowInteropHelper(window).Handle.ToInt64(), first.Text, second.Text)));
            while (await reader.ReadLineAsync() is { } line)
            {
                var request = JsonSerializer.Deserialize<FixtureRequest>(line)!;
                if (request.Action == "close") { window.Close(); break; }
                if (request.Action == "setup")
                {
                    first.IsReadOnly = request.Field == "readonly";
                    first.Text = request.Text;
                    second.Text = "untouched";
                    window.Activate();
                    switch (request.Field)
                    {
                        case "second": second.Focus(); break;
                        case "password": password.Focus(); break;
                        case "button": button.Focus(); break;
                        default: first.Focus(); first.Select(request.Start, request.Length); break;
                    }
                }
                if (request.Action == "second") second.Focus();
                if (request.Action == "lockClipboard" && !OpenClipboard(new WindowInteropHelper(window).Handle))
                    throw new InvalidOperationException("Could not lock clipboard for test");
                if (request.Action == "unlockClipboard") CloseClipboard();
                await writer.WriteLineAsync(JsonSerializer.Serialize(new FixtureState(new WindowInteropHelper(window).Handle.ToInt64(), first.Text, second.Text)));
            }
        };
        return app.Run(window);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();
}
