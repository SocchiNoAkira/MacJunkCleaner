using System.Text;

namespace MacJunkCleaner.Core;

/// <summary>
/// 写日志到 %LOCALAPPDATA%\MacJunkCleaner\logs\，每行立即 flush，
/// 保证任何一次删除动作在发生前就已落盘（最坏情况也能追溯）。
/// </summary>
public sealed class CleanupLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();

    public string FilePath { get; }

    private CleanupLogger(string filePath, StreamWriter writer)
    {
        FilePath = filePath;
        _writer = writer;
    }

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MacJunkCleaner",
        "logs");

    /// <summary>创建日志；失败时返回 null，不影响主流程。</summary>
    public static CleanupLogger? TryCreate(string? directory = null)
    {
        try
        {
            var dir = directory ?? DefaultDirectory;
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"clean-{DateTime.Now:yyyyMMdd-HHmmss}.log");

            var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

            var logger = new CleanupLogger(file, writer);
            logger.Info($"===== 会话开始 {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
            return logger;
        }
        catch
        {
            return null;
        }
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERR ", message);

    private void Write(string level, string message)
    {
        lock (_gate)
        {
            try
            {
                _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}");
            }
            catch
            {
                // 日志失败不能影响清理流程
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try
            {
                Info($"===== 会话结束 {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
                _writer.Dispose();
            }
            catch
            {
                // ignore
            }
        }
    }
}
