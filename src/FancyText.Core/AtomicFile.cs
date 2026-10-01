namespace FancyText.Core;

/// <summary>原子写文件：先写同目录临时文件再整体替换，写到一半崩溃/断电不会留下半截文件，并发读者只会看到旧版或新版。</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }
}
