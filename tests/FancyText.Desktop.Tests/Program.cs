using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FancyText.Core;
using FancyText.Desktop;
using FancyText.Desktop.Helpers;

namespace FancyText.Desktop.Tests;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    [STAThread]
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args is ["--fixture", var pipe]) return InputFixture.Run(pipe);

        var directory = Path.Combine(Path.GetTempPath(), "fancytext-desktop-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        DesktopSettings.FilePathOverrideForTests = Path.Combine(directory, "desktop.json");
        StylePacks.DirectoryOverrideForTests = directory;
        UsageState.StatePathOverrideForTests = Path.Combine(directory, "state.json");
        try
        {
            TestSettings();
            if (args.Contains("--interactive") || args.Contains("--render"))
            {
                var exitCode = 0;
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Startup += async (_, _) =>
                {
                    try
                    {
                        if (args.Contains("--render")) RenderSettings(args[^1]);
                        if (args.Contains("--interactive")) await InputTests.Run();
                    }
                    catch (Exception ex) { Console.Error.WriteLine(ex); exitCode = 1; }
                    finally { app.Shutdown(); }
                };
                app.Run();
                if (exitCode != 0) return exitCode;
            }
            Console.WriteLine($"通过 {_passed} 项桌面测试，失败 {_failed} 项");
            return _failed == 0 ? 0 : 1;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(directory, recursive: true); }
    }

    internal static void Check(bool condition, string name)
    {
        if (!condition)
        {
            _failed++;
            Console.Error.WriteLine("FAIL: " + name);
            return;
        }
        _passed++;
        Console.WriteLine("PASS: " + name);
    }

    private static void TestSettings()
    {
        var path = DesktopSettings.FilePathOverrideForTests!;
        var defaults = DesktopSettings.Load();
        Check(defaults.CopyOnCommit && !defaults.InsertOnCommit, "首次启动保持仅复制");
        File.WriteAllText(path, "{\"hotkey\":\"Ctrl+Alt+F\",\"hideAfterCopy\":false}");
        var legacy = DesktopSettings.Load();
        Check(legacy.CopyOnCommit && !legacy.InsertOnCommit && !legacy.HideAfterCopy, "旧配置保留原行为");
        foreach (var (copy, insert) in new[] { (true, false), (false, true), (true, true) })
        {
            (defaults with { CopyOnCommit = copy, InsertOnCommit = insert }).Save();
            var loaded = DesktopSettings.Load();
            Check(loaded.CopyOnCommit == copy && loaded.InsertOnCommit == insert, $"独立设置持久化：copy={copy}, insert={insert}");
        }
        File.WriteAllText(path, "{\"copyOnCommit\":false,\"insertOnCommit\":false}");
        Check(DesktopSettings.Load().CopyOnCommit, "手改为全关时恢复可用的默认复制");
        File.WriteAllText(path, "{\"copyOnCommit\":null,\"insertOnCommit\":\"true\"}");
        Check(DesktopSettings.Load() is { CopyOnCommit: true, InsertOnCommit: false }, "错误字段类型不会意外开启输入");
        File.WriteAllText(path, "not json");
        Check(DesktopSettings.Load() is { CopyOnCommit: true, InsertOnCommit: false }, "损坏配置恢复默认");
    }

    internal static DesktopSettings TestDefaults => new()
    {
        Hotkey = "Ctrl+Alt+Shift+F12", PrefillSelection = false, PrefillClipboard = false,
        ReduceMotion = true, Backdrop = "solid", HideAfterCopy = false,
    };

    private static void RenderSettings(string output)
    {
        Directory.CreateDirectory(output);
        TestDefaults.Save();
        var usage = new UsageState();
        var main = new MainWindow(usage);
        foreach (var (theme, lang) in new[] { ("light", "zh"), ("dark", "en") })
        {
            usage.SetLanguage(lang);
            main.ApplySettings(TestDefaults with { Theme = theme, CopyOnCommit = true, InsertOnCommit = true });
            var window = new SettingsWindow(main) { Width = 470 };
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(454, 1000));
            content.Arrange(new Rect(0, 0, 454, content.DesiredSize.Height));
            content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(568, (int)Math.Ceiling(content.ActualHeight * 1.25), 120, 120, PixelFormats.Pbgra32);
            // 背景画进快照，保持与 Window.Background 的实际显示一致。
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            }
            bitmap.Render(visual);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, $"settings-{theme}-{lang}.png"));
            encoder.Save(file);
            var options = Descendants<CheckBox>(content).Where(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b).EndsWith("OnCommit", StringComparison.Ordinal)).ToArray();
            Check(options.Length == 2 && options.All(b => b.IsChecked == true && b.IsEnabled), $"设置页双选可用：{theme}/{lang}");
            window.Close();
        }
        main.Close();
    }

    internal static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
