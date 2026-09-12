namespace FancyText.CmdPal.Helpers;

/// <summary>诊断日志（%LOCALAPPDATA%\FancyText\diag.log），页面重建、剪贴板失败等共用。</summary>
internal static class DiagLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FancyText", "diag.log");

    private static readonly Lock Gate = new();

    public static void Append(string line)
    {
        try
        {
            lock (Gate)
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_000_000)
                {
                    File.Delete(LogPath); // 防日志无限增长
                }

                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // 诊断日志失败不影响功能
        }
    }
}
