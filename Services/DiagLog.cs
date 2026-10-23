using System.IO;
using System.Text;

namespace MoyuCast.Services;

/// <summary>
/// 诊断日志：只在设置里打开 Diagnostics 时才写，写入 app.log，超过 2MB 自动滚动。
/// </summary>
public static class DiagLog
{
    private static readonly object Sync = new();
    private const long MaxBytes = 2 * 1024 * 1024;

    public static bool Enabled { get; set; }

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoyuCast", "app.log");

    public static void Write(string line)
    {
        if (!Enabled) return;
        try
        {
            lock (Sync)
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    var backup = Path.Combine(dir, "app.1.log");
                    try { File.Delete(backup); } catch { }
                    try { File.Move(FilePath, backup); } catch { }
                }

                File.AppendAllText(FilePath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {line}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch
        {
            // 日志失败不能影响主流程
        }
    }
}
