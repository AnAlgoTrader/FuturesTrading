using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QQQTrader.Models;
using Ib.Csharp.Api.Client.Implementation.Model;
using IBApi;

namespace QQQTrader.Helper;

public class OrderHelper
{
    private List<OrderPersistence> _activeOrders = new();
    private PositionSummary _position = new();
    private readonly string _ordersPath;
    private readonly string _positionPath;
    private readonly Logger? _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public event Action<IReadOnlyList<OrderPersistence>>? OnOrdersChanged;
    public event Action<PositionSummary>? OnPositionChanged;

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
                    AveragePrice = _position.AveragePrice
                };
            }
        }
    }

    public OrderHelper(Logger? logger = null)
    {
        _logger = logger;
        _ordersPath = Path.Combine(Logger.DATA_DIR, "orders.json");
        _positionPath = Path.Combine(Logger.DATA_DIR, "position.json");
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
            Directory.CreateDirectory(Logger.DATA_DIR);
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
    }

    private async Task SaveActiveOrdersInternal()
    {
        Directory.CreateDirectory(Logger.DATA_DIR);
        var json = JsonSerializer.Serialize(_activeOrders, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_ordersPath, json);
    }

    private async Task SavePositionInternal()
    {
        Directory.CreateDirectory(Logger.DATA_DIR);
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
