using System;
using System.IO;

namespace FootPrintTrader.Model;

public class Logger : IDisposable
{
    private readonly StreamWriter _writer;

    public string DataDirectory { get; }

    public Logger(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        Directory.CreateDirectory(DataDirectory);
        var filePath = Path.Combine(DataDirectory, "footprint.log");
        _writer = new StreamWriter(filePath, append: false) { AutoFlush = true };
    }

    protected Logger(bool silent, string dataDirectory)
    {
        DataDirectory = dataDirectory;
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
