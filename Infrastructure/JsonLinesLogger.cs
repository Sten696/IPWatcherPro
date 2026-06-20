using System.Text.Json;
using System.Text.Json.Serialization;

namespace IPWatcherPro.Infrastructure;

/// <summary>
/// Thread-safe JSON lines logger. Writes one JSON object per line to 
/// %LOCALAPPDATA%\IPWatcherPro\activity.log.
/// </summary>
public sealed class JsonLinesLogger : IDisposable
{
    private readonly string _logPath;
    private readonly StreamWriter _writer;
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _options;
    private bool _disposed;

    public JsonLinesLogger()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IPWatcherPro");

        Directory.CreateDirectory(dir);
        _logPath = Path.Combine(dir, "activity.log");

        // Append mode, UTF-8, auto-flush to prevent data loss on crashes
        _writer = new StreamWriter(_logPath, append: true, System.Text.Encoding.UTF8)
        {
            AutoFlush = true
        };

        _options = new JsonSerializerOptions
        {
            WriteIndented = false, // Crucial for JSON Lines format
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <summary>
    /// Appends a JSON line to the log file. Thread-safe.
    /// </summary>
    public void Write<T>(string type, T data)
    {
        if (_disposed) return;

        lock (_lock)
        {
            if (_disposed) return;
            try
            {
                var entry = new
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Type = type,
                    Data = data
                };
                string json = JsonSerializer.Serialize(entry, _options);
                _writer.WriteLine(json);
            }
            catch
            {
                // Swallow logging errors to prevent crashing the main app
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _writer.Dispose();
        }
    }
}