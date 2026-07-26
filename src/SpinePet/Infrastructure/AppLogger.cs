using System.IO;

namespace SpinePet.Infrastructure;

internal static class AppLogger
{
    private static readonly Lock SyncRoot = new();

    public static void Write(string source, string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            string logPath = Path.Combine(
                AppPaths.LogDirectory,
                $"spinepet-{DateTime.Now:yyyyMMdd}.log");
            string line =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{source}] {message}{Environment.NewLine}";

            lock (SyncRoot)
            {
                File.AppendAllText(logPath, line);
            }
        }
        catch
        {
            // Diagnostics must never interrupt rendering or input handling.
        }
    }
}
