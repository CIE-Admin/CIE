using System.IO;

namespace CIE;

public static class DiagnosticLogger
{
    private static readonly object Sync = new();
    public static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CIE", "logs");

    public static string WriteError(string category, string details)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                var now = DateTimeOffset.Now;
                var file = $"error-{now:yyyyMMdd-HHmmss-fff}.txt";
                var content = $"CIE 错误记录\r\n类型：{category}\r\n时间：{now:O}\r\n{details}{Environment.NewLine}";
                File.WriteAllText(Path.Combine(LogDirectory, file), content);
                File.WriteAllText(Path.Combine(LogDirectory, "last-error.txt"), content);
                return Path.Combine(LogDirectory, file);
            }
        }
        catch { return ""; }
    }
}
