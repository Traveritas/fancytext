using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FancyText.Core;
using FancyText.Desktop;
using FancyText.Desktop.Helpers;
using FancyText.Desktop.Models;

namespace FancyText.Desktop.Tests;

/// <summary>需交互桌面，单独运行 --interactive；所有输入只发给本测试创建的窗口。</summary>
internal static class InputTests
{
    public static async Task Run()
    {
        // 预先实体化全部剪贴板格式；不能完整备份就中止，避免破坏图片/文件等内容。
        var backup = new DataObject();
        if (Clipboard.GetDataObject() is { } original)
        {
            foreach (var format in original.GetFormats(autoConvert: false))
            {
                if (original.GetData(format, autoConvert: false) is { } data) backup.SetData(format, data);
            }
        }
        Program.TestDefaults.Save();
        var main = new MainWindow(new UsageState());
        var pipeName = "fancytext-input-test-" + Guid.NewGuid().ToString("N");
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--fixture");
        start.ArgumentList.Add(pipeName);
        using var process = Process.Start(start)!;
        try
        {
            await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(10));
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            var state = JsonSerializer.Deserialize<FixtureState>((await reader.ReadLineAsync())!)!;
            async Task<FixtureState> Request(FixtureRequest request)
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(request));
                return JsonSerializer.Deserialize<FixtureState>((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)))!)!;
            }
            async Task Setup(string field = "first", string text = "left RIGHT end", int selection = 5, int length = 5)
            {
                main.Hide();
                SetForegroundWindow(new IntPtr(state.Window));
                await Request(new FixtureRequest("setup", field, text, selection, length));
                await Task.Delay(120);
            }
            async Task SelectStyle(bool copy, bool insert, string text = "Hello你好𝄞e\u0301", bool hide = false)
            {
                main.ApplySettings(Program.TestDefaults with { CopyOnCommit = copy, InsertOnCommit = insert, HideAfterCopy = hide });
                main.ShowPopup();
                await Task.Delay(120);
                var input = Program.Descendants<TextBox>(main).First();
                input.Text = text;
                await Task.Delay(220); // 输入预览防抖
                var list = Program.Descendants<ListBox>(main).Single();
                list.SelectedItem = list.Items.Cast<StyleListItem>().Single(i => i.StyleId == "bold");
                list.ScrollIntoView(list.SelectedItem);
                list.UpdateLayout();
                Program.Check(main.IsActive, "选定前弹窗持有焦点");
            }
            string ReadClipboard() => Win32Clipboard.TryGetText(out _) ?? "";
            string Feedback() => Program.Descendants<TextBlock>(main).Single(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "OutputFeedback").Text;

            // 预热跨进程 UIA（冷启动成本不计入之后每次唤出的 300ms 上限）。
            await Setup();
            _ = FocusedTextTarget.Capture();
            await Task.Delay(350);
            foreach (var (copy, insert) in new[] { (true, false), (false, true), (true, true) })
            {
                await Setup();
                Program.Check(Win32Clipboard.TrySetText("clipboard sentinel", out _), "准备剪贴板样本");
                var sequence = GetClipboardSequenceNumber();
                await SelectStyle(copy, insert);
                RaiseEnter(main);
                await Task.Delay(700);
                var actual = await Request(new FixtureRequest("read"));
                var converted = StyleCatalog.All.Single(s => s.Id == "bold").Transform("Hello你好𝄞e\u0301");
                Program.Check(actual.Text == (insert ? "left " + converted + " end" : "left RIGHT end"), $"真实文本框输出 copy={copy} insert={insert}: actual={JsonSerializer.Serialize(actual.Text)}, clipboard={JsonSerializer.Serialize(ReadClipboard())}, visible={main.IsVisible}, feedback={Feedback()}");
                Program.Check(ReadClipboard() == (copy ? converted : "clipboard sentinel"), "剪贴板只按勾选项写入");
                Program.Check(copy || sequence == GetClipboardSequenceNumber(), "仅输入不触碰剪贴板序列号");
                Program.Check(main.IsVisible == !insert, "直接输入收起弹窗；仅复制遵守保留窗口设置");
            }

            await Setup(text: "ab", selection: 1, length: 0);
            await SelectStyle(false, true, "X");
            if (GetForegroundWindow() != new System.Windows.Interop.WindowInteropHelper(main).Handle)
                throw new InvalidOperationException("按键测试要求前台为测试弹窗");
            SendEnter(up: false);
            RaiseEnter(main);
            await Task.Delay(150);
            Program.Check(main.IsVisible && (await Request(new FixtureRequest("read"))).Text == "ab", "按住 Enter 时等待，不提前输入");
            SendEnter(up: false); // 模拟键盘重复按下
            RaiseEnter(main);
            SendEnter(up: true);
            await Task.Delay(700);
            Program.Check((await Request(new FixtureRequest("read"))).Text == "a𝐗b", "松键后光标位置只插入一次，无额外换行");

            await Setup();
            await SelectStyle(false, true, "X");
            var styleList = Program.Descendants<ListBox>(main).Single();
            var container = (ListBoxItem)styleList.ItemContainerGenerator.ContainerFromItem(styleList.SelectedItem);
            styleList.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Control.MouseDoubleClickEvent, Source = container }); // 此事件是 Direct，不从行容器冒泡
            await Task.Delay(700);
            Program.Check((await Request(new FixtureRequest("read"))).Text == "left 𝐗 end", "双击选定使用同一输入行为");

            await Setup(text: "", selection: 0, length: 0);
            await SelectStyle(false, true, "A\r\nB");
            RaiseEnter(main);
            await Task.Delay(700);
            var multiline = (await Request(new FixtureRequest("read"))).Text;
            Program.Check(multiline.Length == 0 && main.IsVisible && Feedback().Length > 0, "多行结果明确提示使用复制，不丢换行后部分输入");

            await Setup();
            await SelectStyle(true, true, "X");
            await Request(new FixtureRequest("lockClipboard"));
            try
            {
                RaiseEnter(main);
                await Task.Delay(700);
                Program.Check((await Request(new FixtureRequest("read"))).Text == "left 𝐗 end", "剪贴板被占用时直接输入仍执行");
                Program.Check(main.IsVisible && Feedback().Contains("剪贴板", StringComparison.Ordinal), "复制失败、输入成功时保留明确提示");
            }
            finally { await Request(new FixtureRequest("unlockClipboard")); }

            await Setup("readonly");
            await SelectStyle(true, true, "X");
            RaiseEnter(main);
            await Task.Delay(400);
            Program.Check(ReadClipboard() == "𝐗" && main.IsVisible && Feedback().Length > 0, "输入不可用时复制仍成功并提示");
            Program.Check((await Request(new FixtureRequest("read"))).Text == "left RIGHT end", "只读文本框保持原文");

            await Setup("button");
            var beforeUnavailable = GetClipboardSequenceNumber();
            await SelectStyle(false, true, "X");
            RaiseEnter(main);
            await Task.Delay(400);
            Program.Check(main.IsVisible && Feedback().Length > 0 && beforeUnavailable == GetClipboardSequenceNumber(), "仅输入且无文本框时保留结果，不自动复制");

            foreach (var field in new[] { "readonly", "password", "button" })
            {
                await Setup(field);
                Program.Check(FocusedTextTarget.Capture() is null, "拒绝非编辑目标：" + field);
            }
            await Setup();
            var target = FocusedTextTarget.Capture();
            Program.Check(target is not null, "捕获原文本框");
            await Request(new FixtureRequest("second"));
            Program.Check(await target!.InsertAsync("must not appear") == InsertOutcome.FocusChanged, "同窗口焦点转到另一文本框时拒绝输入");
            Program.Check((await Request(new FixtureRequest("read"))).OtherText == "untouched", "另一文本框未被误写");

            await Setup();
            target = FocusedTextTarget.Capture();
            Program.Check(await target!.InsertAsync(new string('a', FocusedTextTarget.MaxInputLength + 1)) == InsertOutcome.TooLong, "超长结果明确拒绝，不截断输入");
            Program.Check((await Request(new FixtureRequest("read"))).Text == "left RIGHT end", "超长结果没有部分输入");
            foreach (var text in new[] { "a\tb", "a\nb", "a\u001bb" })
                Program.Check(await target!.InsertAsync(text) == InsertOutcome.UnsupportedText, "控制字符不作为快捷键发送：" + JsonSerializer.Serialize(text));
            await writer.WriteLineAsync(JsonSerializer.Serialize(new FixtureRequest("close")));
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Program.Check(!target.TryActivate(), "目标关闭后拒绝恢复焦点");
        }
        finally
        {
            SendEnter(up: true);
            main.Close();
            if (!process.HasExited) process.Kill();
            Clipboard.SetDataObject(backup, copy: true);
        }
    }

    private static void SendEnter(bool up)
    {
        INPUT[] inputs = [new() { Type = 1, VirtualKey = 0x0D, Flags = up ? 2u : 0u }];
        if (SendInput(1, inputs, Marshal.SizeOf<INPUT>()) != 1) throw new InvalidOperationException("Test key injection failed");
    }
    private static void RaiseEnter(MainWindow main) => main.RaiseEvent(new KeyEventArgs(
        Keyboard.PrimaryDevice, PresentationSource.FromVisual(main), Environment.TickCount, Key.Enter)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ushort VirtualKey;
        [FieldOffset(12)] public uint Flags;
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
