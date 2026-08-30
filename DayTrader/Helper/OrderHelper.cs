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
    private readonly List<OrderPersistence> _orders = new();
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private readonly string _ordersPath;
    private readonly Logger _logger;

    public event Action<IReadOnlyList<OrderPersistence>> OnOrdersChanged;

    public IReadOnlyList<OrderPersistence> Orders
    {
        get
        {
            lock (_stateLock)
            {
                return _orders.ToList();
            }
        }
    }

    /// <summary>
    /// Net position computed from the current persisted fill list using FIFO lot matching.
    /// Direction is determined by the net open position, and AveragePrice represents the
    /// volume-weighted average fill price of the currently open lots. Returns a flat
    /// position (NetQuantity = 0) when there are no fills or the book is balanced.
    /// </summary>
    public PositionSummary GetPosition()
    {
        lock (_stateLock)
        {
            return ComputePosition(_orders);
        }
    }

    private readonly record struct Lot(decimal Quantity, double Price);

    private static PositionSummary ComputePosition(List<OrderPersistence> orders)
    {
        var summary = new PositionSummary();
        if (orders == null || orders.Count == 0)
            return summary;

        var openLots = new List<Lot>();
        string openDirection = "";

        foreach (var order in orders)
        {
            decimal filled = order.FilledQuantity;
            if (filled <= 0 && string.Equals(order.Status, "Filled", StringComparison.OrdinalIgnoreCase))
            {
                filled = order.TotalQuantity;
            }

            if (filled <= 0)
                continue;

            double price = order.AvgFillPrice > 0 ? order.AvgFillPrice : order.LmtPrice;
            string action = order.Action?.ToUpperInvariant() ?? "";

            if (openLots.Count == 0 || openDirection == action)
            {
                openDirection = action;
                openLots.Add(new Lot(filled, price));
            }
            else
            {
                // Opposite direction - closes existing open lots in FIFO order
                decimal remainingToClose = filled;
                while (remainingToClose > 0 && openLots.Count > 0)
                {
                    var firstLot = openLots[0];
                    if (firstLot.Quantity <= remainingToClose)
                    {
                        remainingToClose -= firstLot.Quantity;
                        openLots.RemoveAt(0);
                    }
                    else
                    {
                        openLots[0] = new Lot(firstLot.Quantity - remainingToClose, firstLot.Price);
                        remainingToClose = 0;
                    }
                }

                // If closing order exceeds existing open position, flip direction
                if (remainingToClose > 0)
                {
                    openDirection = action;
                    openLots.Add(new Lot(remainingToClose, price));
                }
            }
        }

        if (openLots.Count == 0)
            return summary;

        decimal totalQty = openLots.Sum(l => l.Quantity);
        if (totalQty <= 0)
            return summary;

        double totalNotional = openLots.Sum(l => (double)l.Quantity * l.Price);
        double avgPrice = totalNotional / (double)totalQty;

        summary.Direction = openDirection;
        summary.Quantity = totalQty;
        summary.AveragePrice = avgPrice;
        summary.NetQuantity = openDirection == "BUY" ? totalQty : -totalQty;

        return summary;
    }

    public OrderHelper(Logger logger = null)
    {
        _logger = logger;
        _ordersPath = Path.Combine(Logger.DATA_DIR, "orders.json");
    }

    public async Task PersistOrder(Order order)
    {
        await _ioLock.WaitAsync();
        try
        {
            lock (_stateLock)
            {
                _orders.Add(new OrderPersistence
                {
                    OrderId = order.OrderId,
                    Action = order.Action,
                    TotalQuantity = order.TotalQuantity,
                    FilledQuantity = 0,
                    OrderType = order.OrderType,
                    LmtPrice = order.LmtPrice,
                    AvgFillPrice = order.LmtPrice,
                    Status = "Unknown"
                });
            }

            _logger?.WriteLine($"[ORDER] Persisted OrderId:{order.OrderId} Action:{order.Action} Qty:{order.TotalQuantity} LmtPrice:{order.LmtPrice}");
            await SaveCurrentOrdersInternal();
            OnOrdersChanged?.Invoke(Orders);
        }
        catch (Exception ex)
        {
            _logger?.WriteLine($"[ORDER] Error persisting order: {ex.Message}");
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task ReadCurrentOrders()
    {
        await _ioLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(Logger.DATA_DIR);
            List<OrderPersistence> loadedOrders = null;
            if (File.Exists(_ordersPath))
            {
                var json = await File.ReadAllTextAsync(_ordersPath);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    loadedOrders = JsonSerializer.Deserialize<List<OrderPersistence>>(json);
                }
            }

            bool cleaned;
            lock (_stateLock)
            {
                _orders.Clear();
                if (loadedOrders != null && loadedOrders.Count > 0)
                {
                    _orders.AddRange(loadedOrders);
                }

                _logger?.WriteLine($"[ORDER] Loaded {_orders.Count} existing order(s) from {_ordersPath}");
                cleaned = CleanOrdersInternal();
            }

            if (cleaned)
            {
                await SaveCurrentOrdersInternal();
            }

            OnOrdersChanged?.Invoke(Orders);
        }
        catch (Exception ex)
        {
            _logger?.WriteLine($"[ORDER] Error reading orders: {ex.Message}");
            lock (_stateLock)
            {
                _orders.Clear();
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task UpdateOrderStatus(OrderStatus status)
    {
        await _ioLock.WaitAsync();
        try
        {
            lock (_stateLock)
            {
                var order = _orders.FirstOrDefault(o => o.OrderId == status.OrderId);
                if (order != null)
                {
                    order.Status = status.Status;
                    order.FilledQuantity = status.Filled;
                    if (status.AvgFillPrice > 0)
                    {
                        order.AvgFillPrice = status.AvgFillPrice;
                    }

                    _logger?.WriteLine($"[ORDER] Status updated: OrderId:{status.OrderId} -> {status.Status} (Filled:{status.Filled}, AvgFillPrice:{status.AvgFillPrice})");
                }
                else
                {
                    _logger?.WriteLine($"[ORDER] Received status for untracked OrderId:{status.OrderId} ({status.Status})");
                }

                CleanOrdersInternal();
            }

            await SaveCurrentOrdersInternal();
            OnOrdersChanged?.Invoke(Orders);
        }
        catch (Exception ex)
        {
            _logger?.WriteLine($"[ORDER] Error updating order status: {ex.Message}");
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private bool CleanOrdersInternal()
    {
        if (_orders.Count == 0)
            return false;

        // If there are pending (non-terminal) orders still active, do not clean yet --
        // a closing order may be in flight and could still change the net position.
        var pendingOrders = _orders.Where(o => !IsTerminalStatus(o.Status)).ToList();
        if (pendingOrders.Count > 0)
            return false;

        // Only clean once the book is net flat. Multiple orders in the same direction
        // accumulate, and a close action sized to the net open size flattens them all.
        var position = ComputePosition(_orders);
        if (position.NetQuantity != 0)
            return false;

        _logger?.WriteLine("[ORDER] Orders cleaned: Net position flat. Resetting active orders.");
        _orders.Clear();
        return true;
    }

    private static bool IsTerminalStatus(string status)
    {
        return status is "Filled" or "Cancelled" or "Inactive" or "ApiCancelled";
    }

    private async Task SaveCurrentOrdersInternal()
    {
        Directory.CreateDirectory(Logger.DATA_DIR);
        string json;
        lock (_stateLock)
        {
            json = JsonSerializer.Serialize(_orders, new JsonSerializerOptions { WriteIndented = true });
        }
        await File.WriteAllTextAsync(_ordersPath, json);
    }
}