using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DayTrader.Models;
using Ib.Csharp.Api.Client.Implementation.Model;
using IBApi;

namespace DayTrader.Helper;

public class OrderHelper
{
    private List<OrderPersistence> _activeOrders = new();
    private PositionSummary _position = new();
    private readonly string _ordersPath;
    private readonly string _positionPath;
    private readonly Logger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

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
                    AveragePrice = _position.AveragePrice
                };
            }
        }
    }

    public OrderHelper(Logger logger = null)
    {
        _logger = logger;
        _ordersPath = Path.Combine(Logger.DATA_DIR, "orders.json");
        _positionPath = Path.Combine(Logger.DATA_DIR, "position.json");
        _logger?.WriteLine($"[ORDER] Initialized. OrdersPath:{_ordersPath} PositionPath:{_positionPath}");
    }

    public async Task PersistOrder(Order order)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger?.WriteLine($"[ORDER] PersistOrder ENTER OrderId:{order.OrderId} Action:{order.Action} Qty:{order.TotalQuantity} Type:{order.OrderType} Price:{order.LmtPrice} ActiveBefore:{_activeOrders.Count}");
        await _lock.WaitAsync();
        var lockMs = sw.ElapsedMilliseconds;
        try
        {
            var existing = _activeOrders.FirstOrDefault(o => o.OrderId == order.OrderId);
            if (existing != null)
                _logger?.WriteLine($"[ORDER] WARN: OrderId:{order.OrderId} already in active list! Existing: Status:{existing.Status} Action:{existing.Action} Qty:{existing.TotalQuantity}");

            _activeOrders.Add(new OrderPersistence
            {
                OrderId = order.OrderId,
                Action = order.Action,
                TotalQuantity = order.TotalQuantity,
                OrderType = order.OrderType,
                LmtPrice = order.LmtPrice,
                Status = "Unknown"
            });
            _logger?.WriteLine($"[ORDER] PersistOrder ADDED OrderId:{order.OrderId} ActiveAfter:{_activeOrders.Count} ActiveIds:[{string.Join(",", _activeOrders.Select(o => o.OrderId))}] LockWait:{lockMs}ms");
            await SaveActiveOrdersInternal();
            OnOrdersChanged?.Invoke(ActiveOrders);
        }
        catch (Exception ex)
        {
            _logger?.WriteLine($"[ORDER] PersistOrder ERROR OrderId:{order.OrderId} {ex}");
        }
        finally
        {
            _lock.Release();
            _logger?.WriteLine($"[ORDER] PersistOrder EXIT OrderId:{order.OrderId} TotalMs:{sw.ElapsedMilliseconds}");
        }
    }

    public async Task ReadCurrentState()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger?.WriteLine("[ORDER] ReadCurrentState ENTER");
        await _lock.WaitAsync();
        var lockMs = sw.ElapsedMilliseconds;
        try
        {
            Directory.CreateDirectory(Logger.DATA_DIR);
            _activeOrders = await ReadOrdersInternal();
            _position = await ReadPositionInternal();
            _logger?.WriteLine($"[ORDER] ReadCurrentState LOADED ActiveOrders:{_activeOrders.Count} ActiveIds:[{string.Join(",", _activeOrders.Select(o => $"{o.OrderId}({o.Action}@{o.Status})"))}] Position:{Describe(_position)} LockWait:{lockMs}ms");
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
            _logger?.WriteLine($"[ORDER] ReadCurrentState EXIT TotalMs:{sw.ElapsedMilliseconds}");
        }
    }

    public async Task UpdateOrderStatus(OrderStatus status)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger?.WriteLine($"[ORDER] UpdateOrderStatus ENTER OrderId:{status.OrderId} Status:{status.Status} FillQty:{status.Filled} AvgFill:{status.AvgFillPrice:F2} ActiveBefore:{_activeOrders.Count} ActiveIds:[{string.Join(",", _activeOrders.Select(o => o.OrderId))}]");
        await _lock.WaitAsync();
        var lockMs = sw.ElapsedMilliseconds;
        try
        {
            var order = _activeOrders.FirstOrDefault(o => o.OrderId == status.OrderId);
            if (order == null)
            {
                _logger?.WriteLine($"[ORDER] UpdateOrderStatus UNTRACKED OrderId:{status.OrderId} Status:{status.Status} — no matching active order found. LockWait:{lockMs}ms");
                return;
            }

            var prevStatus = order.Status;
            var prevFillQty = order.FilledQuantity;
            var prevFillPrice = order.AvgFillPrice;

            order.Status = status.Status;
            order.AvgFillPrice = status.AvgFillPrice > 0 ? status.AvgFillPrice : order.AvgFillPrice;
            order.FilledQuantity = status.Filled > 0 ? status.Filled : order.FilledQuantity;
            _logger?.WriteLine($"[ORDER] UpdateOrderStatus UPDATED OrderId:{status.OrderId} {prevStatus}->{order.Status} FillQty:{prevFillQty}->{order.FilledQuantity} AvgFill:{prevFillPrice:F2}->{order.AvgFillPrice:F2} LockWait:{lockMs}ms");

            if (IsTerminalStatus(status.Status))
            {
                _logger?.WriteLine($"[ORDER] UpdateOrderStatus TERMINAL OrderId:{order.OrderId} Action:{order.Action} Status:{status.Status} FilledQty:{order.FilledQuantity} FilledPrice:{order.AvgFillPrice:F2}");

                if (string.Equals(status.Status, "Filled", StringComparison.OrdinalIgnoreCase) &&
                    order.FilledQuantity > 0)
                {
                    var fillPrice = order.AvgFillPrice > 0 ? order.AvgFillPrice : order.LmtPrice;
                    _logger?.WriteLine($"[POSITION] ApplyFill ENTER Action:{order.Action} FilledQty:{order.FilledQuantity} FillPrice:{fillPrice:F2} PositionBefore:{Describe(_position)}");
                    ApplyFillInternal(order.Action, order.FilledQuantity, fillPrice);
                    _logger?.WriteLine($"[POSITION] ApplyFill DONE PositionAfter:{Describe(_position)}");
                    await SavePositionInternal();
                }

                _activeOrders.Remove(order);
                _logger?.WriteLine($"[ORDER] UpdateOrderStatus REMOVED OrderId:{order.OrderId} ActiveAfter:{_activeOrders.Count} ActiveIds:[{string.Join(",", _activeOrders.Select(o => o.OrderId))}]");
                await SaveActiveOrdersInternal();
                OnOrdersChanged?.Invoke(ActiveOrders);
                OnPositionChanged?.Invoke(Position);
            }
        }
        catch (Exception ex)
        {
            _logger?.WriteLine($"[ORDER] UpdateOrderStatus ERROR OrderId:{status.OrderId} {ex}");
        }
        finally
        {
            _lock.Release();
            _logger?.WriteLine($"[ORDER] UpdateOrderStatus EXIT OrderId:{status.OrderId} TotalMs:{sw.ElapsedMilliseconds}");
        }
    }

    /// <summary>
    /// Applies a fill (with sign from action) to the aggregated net position,
    /// updating the average entry price as the position grows, shrinks, or flips.
    /// </summary>
    private void ApplyFillInternal(string action, decimal filledQty, double fillPrice)
    {
        decimal sign = string.Equals(action, "BUY", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
        _logger?.WriteLine($"[POSITION] ApplyFillInternal Action:{action} Sign:{sign} FilledQty:{filledQty} FillPrice:{fillPrice:F2} NetQtyBefore:{_position.NetQuantity} AvgPriceBefore:{_position.AveragePrice:F2} DirBefore:{_position.Direction}");

        if (filledQty <= 0 || fillPrice <= 0)
        {
            _logger?.WriteLine($"[POSITION] ApplyFillInternal SKIP: FilledQty:{filledQty} or FillPrice:{fillPrice:F2} is non-positive");
            return;
        }

        var q = _position.NetQuantity;
        var a = _position.AveragePrice;
        var newQ = q + sign * filledQty;

        double newAvg;
        string reason;
        if (q == 0)
        {
            newAvg = fillPrice;
            reason = "FLAT->OPEN";
        }
        else if (newQ == 0)
        {
            newAvg = 0;
            reason = "CLOSE->FLAT";
        }
        else if (Math.Sign(newQ) == Math.Sign(q))
        {
            if (Math.Abs(newQ) > Math.Abs(q))
            {
                newAvg = (a * (double)Math.Abs(q) + fillPrice * (double)filledQty) / (double)Math.Abs(newQ);
                reason = "ADD_TO_POSITION";
            }
            else
            {
                newAvg = a;
                reason = "REDUCE_SAME_DIR";
            }
        }
        else
        {
            newAvg = fillPrice;
            reason = "FLIP_DIRECTION";
        }

        _position.NetQuantity = newQ;
        _position.AveragePrice = newAvg;
        _position.Quantity = Math.Abs(newQ);
        _position.Direction = newQ > 0 ? "BUY" : newQ < 0 ? "SELL" : "";

        _logger?.WriteLine($"[POSITION] ApplyFillInternal RESULT Reason:{reason} NetQty:{q}->{_position.NetQuantity} AvgPrice:{a:F2}->{_position.AveragePrice:F2} Dir:{_position.Direction} Notional:{(double)_position.NetQuantity * _position.AveragePrice * 2.0:F2}");
    }

    private async Task SaveActiveOrdersInternal()
    {
        Directory.CreateDirectory(Logger.DATA_DIR);
        var json = JsonSerializer.Serialize(_activeOrders, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_ordersPath, json);
        _logger?.WriteLine($"[ORDER] SaveActiveOrders DONE Count:{_activeOrders.Count} ActiveIds:[{string.Join(",", _activeOrders.Select(o => o.OrderId))}]");
    }

    private async Task SavePositionInternal()
    {
        Directory.CreateDirectory(Logger.DATA_DIR);
        var json = JsonSerializer.Serialize(_position, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_positionPath, json);
        _logger?.WriteLine($"[POSITION] SavePosition DONE {Describe(_position)}");
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

    private static string Describe(PositionSummary p)
    {
        double totalDollar = p.NetQuantity != 0 ? (double)p.NetQuantity * p.AveragePrice * 2.0 : 0;
        return p.NetQuantity == 0
            ? "FLAT"
            : $"{p.Direction} {Math.Abs(p.NetQuantity)} @ {p.AveragePrice:F2} (notional ${totalDollar:F2})";
    }
}
