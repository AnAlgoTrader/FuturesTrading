using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DayTrader.Models;
using Ib.Csharp.Api.Client.Implementation.Model;
using IBApi;

namespace DayTrader.Helper;

public class OrderHelper
{
    private List<OrderPersistence> _orders = new();
    private readonly string _ordersPath;

    public OrderHelper()
    {
        _ordersPath = Path.Combine(Logger.DATA_DIR, "orders.json");
    }
    
    public async Task PersistOrder(Order order)
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
        await SaveCurrentOrders();
    }
    
    public async Task ReadCurrentOrders()
    {
        await using var openStream = File.OpenRead(_ordersPath);
        _orders = await JsonSerializer.DeserializeAsync<List<OrderPersistence>>(openStream);
        await CleanOrders();
    }

    private async Task CleanOrders()
    {
        if (_orders.Count == 2)
        {
            var order1 = _orders[0];
            var order2 = _orders[1];

            if (order1.Status == "Filled" && order2.Status == "Filled" && order1.Action != order2.Action)
            {
                _orders = new List<OrderPersistence>();
                await SaveCurrentOrders();
            }
        } 
    }

    private async Task SaveCurrentOrders()
    {
        await using var createStream = File.Create(_ordersPath);
        await JsonSerializer.SerializeAsync(createStream, _orders, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task UpdateOrderStatus(OrderStatus status)
    {
        foreach (var order in _orders)
        {
            if (order.OrderId == status.OrderId)
            {
                order.Status = status.Status;
            }
        }
        await CleanOrders();
        await SaveCurrentOrders();
    }
}