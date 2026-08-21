using System;
using System.IO;

namespace Trader.Models;

public class Logger : IDisposable
{
    private readonly StreamWriter _writer;

    public Logger(string filePath)
    {
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
