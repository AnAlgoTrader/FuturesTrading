using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DayTrader.Models;

namespace DayTrader.Helper;

public class MagnetLevelReader : IDisposable
{
    public const string DefaultPath = "/home/oce/LocalDB/Options/ndx/NDX.magnets.json";
    private readonly string _filePath;
    private readonly TimeSpan _interval;
    private readonly double _minPercentage;
    private readonly Logger _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task _timerTask;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public event Action<IReadOnlyList<MagnetLevel>> OnLevelsUpdated;

    public MagnetLevelReader(
        Logger logger = null,
        string filePath = DefaultPath,
        TimeSpan? interval = null,
        double minPercentage = 20.0)
    {
        _logger = logger;
        _filePath = filePath;
        _interval = interval ?? TimeSpan.FromMinutes(5);
        _minPercentage = minPercentage;
    }

    public void Start()
    {
        _timerTask = RunPeriodicCheckAsync(_cts.Token);
    }

    private async Task RunPeriodicCheckAsync(CancellationToken ct)
    {
        // 1. Initial read immediately upon startup
        await ReadAndNotifyAsync();

        // 2. Periodic tick every 5 minutes
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
            {
                await ReadAndNotifyAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown/cancellation
        }
        catch (Exception ex)
        {
            _logger?.WriteLine($"[MAGNETS] Periodic timer error: {ex.Message}");
        }
    }

    public async Task<List<MagnetLevel>> ReadAndNotifyAsync()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger?.WriteLine($"[MAGNETS] File not found: {_filePath}");
                return new List<MagnetLevel>();
            }

            await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var allLevels = await JsonSerializer.DeserializeAsync<List<MagnetLevel>>(stream, JsonOptions);

            if (allLevels == null)
            {
                _logger?.WriteLine($"[MAGNETS] Empty/invalid JSON in {_filePath}");
                return new List<MagnetLevel>();
            }

            var filtered = allLevels
                .Where(l => l.Percentage > _minPercentage && l.Strike > 0)
                .OrderByDescending(l => l.Percentage)
                .ToList();

            _logger?.WriteLine($"[MAGNETS] Loaded {filtered.Count} levels (>{_minPercentage}%) from {_filePath}: {string.Join(", ", filtered.Select(l => $"{l.Strike} ({l.Percentage:F1}%)"))}");
            OnLevelsUpdated?.Invoke(filtered);
            return filtered;
        }
        catch (Exception ex)
        {
            _logger?.WriteLine($"[MAGNETS] Failed to read {_filePath}: {ex.Message}");
            return new List<MagnetLevel>();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
