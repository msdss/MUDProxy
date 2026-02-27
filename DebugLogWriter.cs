namespace MudProxyViewer;

/// <summary>
/// Writes timestamped debug log entries to a per-session file.
/// 
/// Each session gets its own file named {prefix}-{timestamp}.log
/// stored in %AppData%\MudProxyViewer\Logs\.
/// 
/// Thread-safe. All write errors are silently swallowed to prevent
/// debug logging from ever crashing the application.
/// 
/// Usage:
///   var log = DebugLogWriter.Create("walk");   // creates walk-2026-02-22_13-02-35.log
///   log.Write("STEP", "cmd=n toKey=1/100");
///   log.Close();                                // optional, flushes and closes
/// </summary>
public class DebugLogWriter : IDisposable
{
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private readonly string _filePath;
    private bool _disposed;

    private DebugLogWriter(string filePath)
    {
        _filePath = filePath;
        try
        {
            _writer = new StreamWriter(filePath, append: false) { AutoFlush = true };
        }
        catch
        {
            _writer = null;
        }
    }

    /// <summary>
    /// Create a new debug log file with the given prefix.
    /// File: %AppData%\MudProxyViewer\Logs\{prefix}-{timestamp}.log
    /// </summary>
    public static DebugLogWriter Create(string prefix)
    {
        var dir = GetLogsDirectory();
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var filePath = Path.Combine(dir, $"{prefix}-{timestamp}.log");
        return new DebugLogWriter(filePath);
    }

    /// <summary>
    /// Get the Logs directory, creating it if necessary.
    /// </summary>
    public static string GetLogsDirectory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MudProxyViewer",
            "Logs");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Write a timestamped entry to the log file.
    /// </summary>
    public void Write(string message)
    {
        if (_disposed) return;
        try
        {
            lock (_lock)
            {
                _writer?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
            }
        }
        catch { /* Never crash for logging */ }
    }

    /// <summary>
    /// Write a timestamped entry with a category tag.
    /// Example: Write("STEP", "cmd=n toKey=1/100") → "[13:02:35.123] [STEP] cmd=n toKey=1/100"
    /// </summary>
    public void Write(string tag, string message)
    {
        Write($"[{tag}] {message}");
    }

    /// <summary>
    /// Close the log file. Safe to call multiple times.
    /// </summary>
    public void Close()
    {
        if (_disposed) return;
        try
        {
            lock (_lock)
            {
                _writer?.Flush();
                _writer?.Close();
                _writer?.Dispose();
                _writer = null;
            }
        }
        catch { /* Never crash for logging */ }
        _disposed = true;
    }

    public void Dispose()
    {
        Close();
    }

    /// <summary>
    /// The full path to the log file, for reference.
    /// </summary>
    public string FilePath => _filePath;
}
