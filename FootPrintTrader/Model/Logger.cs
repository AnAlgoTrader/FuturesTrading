using System;
using System.IO;

namespace FootPrintTrader.Model;

public class Logger : IDisposable
{
    public const string DATA_DIR = "/home/oce/LocalDB/logs/footprint";
    private readonly StreamWriter _writer;

    public Logger()
    {
        Directory.CreateDirectory(DATA_DIR);
        var filePath = Path.Combine(DATA_DIR, "footprint.log");
        _writer = new StreamWriter(filePath, append: false) { AutoFlush = true };
    }

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