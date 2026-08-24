using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Common;

public class OrderStore
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _options;

    public OrderStore(string filePath)
    {
        _filePath = filePath;
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public TrackedOrder Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath))
                return null;
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<TrackedOrder>(json, _options);
        }
    }

    public void Save(TrackedOrder order)
    {
        lock (_lock)
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(order, _options));
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
    }
}
