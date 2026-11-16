using System.Text;

namespace DynamicIsland.Windows.Services;

public sealed class LoggingService
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Infrastructure.OrderedBackgroundWork> Writers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Infrastructure.OrderedBackgroundWork _writes;
    private readonly string _logPath;
    private bool _debugEnabled;

    public LoggingService()
    {
        var directory = Path.Combine(DynamicIsland.Windows.Infrastructure.AppDataPaths.Root, "logs");
        Directory.CreateDirectory(directory);
        _logPath = Path.Combine(directory, "dynamic-island.log");
        _writes = Writers.GetOrAdd(_logPath, _ => new());
    }

    public string LogPath => _logPath;
    public void SetDebugEnabled(bool enabled) => _debugEnabled = enabled;
    public void Info(string message) => Write("INFO", message);
    public void Debug(string message) { if (_debugEnabled) Write("DEBUG", message); }
    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}: {exception}");

    private void Write(string level, string message)
    {
        // Preserve event time and ordering without making input or audio callbacks wait for disk.
        var sanitized = message.Replace('\r', ' ').Replace('\n', ' ');
        var line = $"{DateTimeOffset.Now:O} [{level}] {sanitized}{Environment.NewLine}";
        _writes.Enqueue(() =>
        {
            try
            {
                RotateIfNeeded();
                File.AppendAllText(_logPath, line, Encoding.UTF8);
            }
            catch { }
            return Task.CompletedTask;
        });
    }

    public Task FlushAsync() => _writes.FlushAsync();

    private void RotateIfNeeded()
    {
        var info = new FileInfo(_logPath);
        if (!info.Exists || info.Length < 2_000_000) return;
        var previous = _logPath + ".1";
        File.Move(_logPath, previous, true);
    }
}
