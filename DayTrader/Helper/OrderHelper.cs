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
    private List<OrderPersistence> _orders = new();
    private readonly string _ordersPath;
    private readonly Logger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public IReadOnlyList<OrderPersistence> Orders
    {
        get
        {
            lock (_orders)
            {
                return _orders.ToList();
            }
        }
    }

    public OrderHelper(Logger logger = null)
    {
        _logger = logger;
        _ordersPath = Path.Combine(Logger.DATA_DIR, "orders.json");
    }
    
    public async Task PersistOrder(Order order)
    {
        await _lock.WaitAsync();
        try
        {
            _orders.Add(new OrderPersistence
            {
                OrderId = order.OrderId,
                Action = order.Action,
                TotalQuantity = order.TotalQuantity,
                OrderType = order.OrderType,
                LmtPrice = order.LmtPrice,
                Status = "Unknown"
            });
            _logger?.WriteLine($"[ORDER] Persisted OrderId:{order.OrderId} Action:{order.Action} Qty:{order.TotalQuantity} LmtPrice:{order.LmtPrice}");
            await SaveCurrentOrdersInternal();
        }
        catch (Exception ex)
        {
            _logger?.WriteLine($"[ORDER] Error persisting order: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task ReadCurrentOrders()
    {
        await _lock.WaitAsync();
        try
        {
            Directory.CreateDirectory(Logger.DATA_DIR);
            if (File.Exists(_ordersPath))
            {
                var json = await File.ReadAllTextAsync(_ordersPath);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    _orders = JsonSerializer.Deserialize<List<OrderPersistence>>(json) ?? new List<OrderPersistence>();
                }
                else
                {
                    _orders = new List<OrderPersistence>();
                }
            }
            else
            {
                _orders = new List<OrderPersistence>();
            }

            _logger?.WriteLine($"[ORDER] Loaded {_orders.Count} existing order(s) from {_ordersPath}");

            bool cleaned = CleanOrdersInternal();
            if (cleaned)
            {
                await SaveCurrentOrdersInternal();
            }
        }
        catch (Exception ex)
        {
            _logger?.WriteLine($"[ORDER] Error reading orders: {ex.Message}");
            _orders = new List<OrderPersistence>();
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
            var order = _orders.FirstOrDefault(o => o.OrderId == status.OrderId);
            if (order != null)
            {
                order.Status = status.Status;
                _logger?.WriteLine($"[ORDER] Status updated: OrderId:{status.OrderId} -> {status.Status}");
            }
            else
            {
                _logger?.WriteLine($"[ORDER] Received status for untracked OrderId:{status.OrderId} ({status.Status})");
            }

            CleanOrdersInternal();
            await SaveCurrentOrdersInternal();
        }
        catch (Exception ex)
        {
            _logger?.WriteLine($"[ORDER] Error updating order status: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool CleanOrdersInternal()
    {
        if (_orders.Count == 0)
            return false;

        var filledOrders = _orders.Where(o => string.Equals(o.Status, "Filled", StringComparison.OrdinalIgnoreCase)).ToList();
        var pendingOrders = _orders.Where(o => !IsTerminalStatus(o.Status)).ToList();

        // If there are pending orders still active, do not clean yet
        if (pendingOrders.Count > 0)
            return false;

        // Calculate total filled Buy qty vs total filled Sell qty
        decimal buyFilledQty = filledOrders.Where(o => string.Equals(o.Action, "BUY", StringComparison.OrdinalIgnoreCase)).Sum(o => o.TotalQuantity);
        decimal sellFilledQty = filledOrders.Where(o => string.Equals(o.Action, "SELL", StringComparison.OrdinalIgnoreCase)).Sum(o => o.TotalQuantity);

        // If a full round-trip occurred (buys equal sells > 0) or all orders were cancelled/inactive
        if ((buyFilledQty > 0 && buyFilledQty == sellFilledQty) || (filledOrders.Count == 0 && _orders.Count > 0))
        {
            _logger?.WriteLine($"[ORDER] Orders cleaned: Round-trip complete (BUY:{buyFilledQty}, SELL:{sellFilledQty}). Resetting active orders.");
            _orders.Clear();
            return true;
        }

        return false;
    }

    private static bool IsTerminalStatus(string status)
    {
        return status is "Filled" or "Cancelled" or "Inactive" or "ApiCancelled";
    }

    private async Task SaveCurrentOrdersInternal()
    {
        Directory.CreateDirectory(Logger.DATA_DIR);
        var json = JsonSerializer.Serialize(_orders, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_ordersPath, json);
    }
}