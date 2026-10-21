using System.Diagnostics;

namespace PcDashboard.Core;

/// <summary>%LOCALAPPDATA%\WinDash\log.txt dosyasına basit günlük.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(AppPaths.DataDirectory, "log.txt");

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        Debug.WriteLine(line);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 2 * 1024 * 1024) info.Delete();
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Günlük yazılamazsa uygulama çalışmaya devam etsin.
        }
    }
}
