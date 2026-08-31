using System;
using System.IO;

namespace DayTrader.Helper;

public class Logger : IDisposable
{
    public const string DATA_DIR = "/home/oce/LocalDB/logs/futures";
    private readonly StreamWriter? _writer;

    public Logger()
    {
        Directory.CreateDirectory(DATA_DIR);
        var filePath = Path.Combine(DATA_DIR, "daytrader.log");
        _writer = new StreamWriter(filePath, append: false) { AutoFlush = true };
    }

    /// <summary>
    /// Creates a silent logger that discards all output.
    /// Used by parameter sweep tuner to avoid log I/O overhead.
    /// </summary>
    protected Logger(bool silent)
    {
        _writer = null;
    }

    public void WriteLine(string message)
    {
        if (_writer == null) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
        _writer.WriteLine(line);
    }

    public void Dispose()
    {
        _writer?.Dispose();
    }
}

/// <summary>
/// Silent logger that discards all output — for parameter sweeps.
/// </summary>
public class SilentLogger : Logger
{
    public SilentLogger() : base(silent: true) { }
}
