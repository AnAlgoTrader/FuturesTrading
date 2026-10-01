using System;
using System.IO;

namespace Trader.Model;

public class Logger : IDisposable
{
    private const string DATA_DIR = "/home/oce/LocalDB/logs/daytrader";
    private readonly StreamWriter _writer;

    public Logger()
    {
        Directory.CreateDirectory(DATA_DIR);
        var filePath = Path.Combine(DATA_DIR, "daytrader.log");
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