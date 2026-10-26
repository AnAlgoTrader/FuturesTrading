using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ib.Csharp.Api.Client.Implementation.Model;
using IBApi;

namespace FootPrintTrader.Model;

public class OrderHelper
{
    private List<OrderPersistence> _activeOrders = new();
    private PositionSummary _position = new();
    private readonly string _dataDirectory;
    private readonly string _ordersPath;
    private readonly string _positionPath;
    private readonly Logger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public double CurrentChartPrice { get; set; }

    public event Action<IReadOnlyList<OrderPersistence>> OnOrdersChanged;
    public event Action<PositionSummary> OnPositionChanged;

    public IReadOnlyList<OrderPersistence> ActiveOrders
    {
        get
        {
            lock (_activeOrders)
            {
                return _activeOrders.ToList();
            }
        }
    }

    public PositionSummary Position
    {
        get
        {
            lock (_position)
            {
                return new PositionSummary
                {
                    NetQuantity = _position.NetQuantity,
                    Direction = _position.Direction,
                    Quantity = _position.Quantity,
                    AveragePrice = _position.AveragePrice,
                    ChartEntryPrice = _position.ChartEntryPrice
                };
            }
        }
    }

    public OrderHelper(Logger logger = null, string dataDir = null)
    {
        _logger = logger;
        _dataDirectory = string.IsNullOrWhiteSpace(dataDir) ? (logger?.DataDirectory ?? MarketProfile.Current.DataDirectory) : dataDir;
        _ordersPath = Path.Combine(_dataDirectory, "orders.json");
        _positionPath = Path.Combine(_dataDirectory, "position.json");
    }

    public async Task PersistOrder(Order order)
    {
        await _lock.WaitAsync();
        try
        {
            _activeOrders.Add(new OrderPersistence
            {
                OrderId = order.OrderId,
                Action = order.Action,
                TotalQuantity = order.TotalQuantity,
                OrderType = order.OrderType,
                LmtPrice = order.LmtPrice,
                Status = "Unknown"
            });
            await SaveActiveOrdersInternal();
            OnOrdersChanged?.Invoke(ActiveOrders);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ReadCurrentState()
    {
        await _lock.WaitAsync();
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            _activeOrders = await ReadOrdersInternal();
            _position = await ReadPositionInternal();
            OnOrdersChanged?.Invoke(ActiveOrders);
            OnPositionChanged?.Invoke(Position);
        }
        catch (Exception ex)
        {
            _logger?.WriteLine($"[ORDER] ReadCurrentState ERROR: {ex}");
            _activeOrders = new List<OrderPersistence>();
            _position = new PositionSummary();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateOrderStatus(OrderStatus status)
    {
        await _lock.WaitAsync();
        try
        {
            var order = _activeOrders.FirstOrDefault(o => o.OrderId == status.OrderId);
            if (order == null) return;

            order.Status = status.Status;
            order.AvgFillPrice = status.AvgFillPrice > 0 ? status.AvgFillPrice : order.AvgFillPrice;
            order.FilledQuantity = status.Filled > 0 ? status.Filled : order.FilledQuantity;

            if (IsTerminalStatus(status.Status))
            {
                if (string.Equals(status.Status, "Filled", StringComparison.OrdinalIgnoreCase) &&
                    order.FilledQuantity > 0 && !string.IsNullOrEmpty(order.Action))
                {
                    var fillPrice = order.AvgFillPrice > 0 ? order.AvgFillPrice : order.LmtPrice;
                    ApplyFillInternal(order.Action, order.FilledQuantity, fillPrice);
                    await SavePositionInternal();
                }

                _activeOrders.Remove(order);
                await SaveActiveOrdersInternal();
                OnOrdersChanged?.Invoke(ActiveOrders);
                OnPositionChanged?.Invoke(Position);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private void ApplyFillInternal(string action, decimal filledQty, double fillPrice)
    {
        decimal sign = string.Equals(action, "BUY", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
        var q = _position.NetQuantity;
        var a = _position.AveragePrice;
        var newQ = q + sign * filledQty;
        double newAvg;

        if (q == 0)
        {
            newAvg = fillPrice;
        }
        else if (newQ == 0)
        {
            newAvg = 0;
        }
        else if (Math.Sign(newQ) == Math.Sign(q))
        {
            if (Math.Abs(newQ) > Math.Abs(q))
                newAvg = (a * (double)Math.Abs(q) + fillPrice * (double)filledQty) / (double)Math.Abs(newQ);
            else
                newAvg = a;
        }
        else
        {
            newAvg = fillPrice;
        }

        _position.NetQuantity = newQ;
        _position.AveragePrice = newAvg;
        _position.Quantity = Math.Abs(newQ);
        _position.Direction = newQ > 0 ? "BUY" : newQ < 0 ? "SELL" : "";

        if (newQ == 0)
        {
            _position.ChartEntryPrice = 0;
        }
        else if (q == 0 || Math.Sign(newQ) != Math.Sign(q))
        {
            _position.ChartEntryPrice = CurrentChartPrice;
        }
        else if (Math.Abs(newQ) > Math.Abs(q))
        {
            _position.ChartEntryPrice =
                (_position.ChartEntryPrice * (double)Math.Abs(q) + CurrentChartPrice * (double)filledQty)
                / (double)Math.Abs(newQ);
        }
    }

    private async Task SaveActiveOrdersInternal()
    {
        Directory.CreateDirectory(_dataDirectory);
        var json = JsonSerializer.Serialize(_activeOrders, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_ordersPath, json);
    }

    private async Task SavePositionInternal()
    {
        Directory.CreateDirectory(_dataDirectory);
        var json = JsonSerializer.Serialize(_position, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_positionPath, json);
    }

    private async Task<List<OrderPersistence>> ReadOrdersInternal()
    {
        if (File.Exists(_ordersPath))
        {
            var json = await File.ReadAllTextAsync(_ordersPath);
            if (!string.IsNullOrWhiteSpace(json))
                return JsonSerializer.Deserialize<List<OrderPersistence>>(json) ?? new List<OrderPersistence>();
        }
        return new List<OrderPersistence>();
    }

    private async Task<PositionSummary> ReadPositionInternal()
    {
        if (File.Exists(_positionPath))
        {
            var json = await File.ReadAllTextAsync(_positionPath);
            if (!string.IsNullOrWhiteSpace(json))
                return JsonSerializer.Deserialize<PositionSummary>(json) ?? new PositionSummary();
        }
        return new PositionSummary();
    }

    private static bool IsTerminalStatus(string status)
    {
        return status is "Filled" or "Cancelled" or "Inactive" or "ApiCancelled";
    }
}
