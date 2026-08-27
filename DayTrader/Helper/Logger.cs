using System;
using System.IO;

namespace DayTrader.Helper;

public class Logger : IDisposable
{
    public const string DATA_DIR = "/home/oce/LocalDB/logs/futures";
    private readonly StreamWriter _writer;

    public Logger()
    {
        Directory.CreateDirectory(DATA_DIR);
        var filePath = Path.Combine(DATA_DIR, "daytrader.log");
        _writer = new StreamWriter(filePath, append: false) { AutoFlush = true };
    }

    public void WriteLine(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
        _writer.WriteLine(line);
    }

    public void Dispose()
    {
        _writer.Dispose();
    }
}
