using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;

namespace FancyText.CmdPal.Helpers;

/// <summary>基于 WinRT DataTransfer 的剪贴板读写（扩展进程内使用）。</summary>
internal static class ClipboardBridge
{
    public static void SetText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        global::Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
    }

    public static string? TryGetText()
    {
        try
        {
            var content = global::Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (content is null || !content.Contains(StandardDataFormats.Text))
            {
                return null;
            }

            // GetTextAsync 不能在 ASTA 线程上同步等待，切到线程池
            return Task.Run(() => content.GetTextAsync().AsTask()).GetAwaiter().GetResult();
        }
        catch (COMException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
