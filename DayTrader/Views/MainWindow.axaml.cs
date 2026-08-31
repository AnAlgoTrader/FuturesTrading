using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using DayTrader.Helper;
using DayTrader.Models;
using DayTrader.ViewModels;

namespace DayTrader.Views;

public partial class MainWindow : Window
{
    private readonly NativeWebView _chart;
    private readonly List<(Bar Bar, string Color)> _pendingCandles = new();
    private (double Price, string Color, string Title)? _pendingOrderLine;
    private IReadOnlyList<MagnetLevel> _pendingMagnetLevels;
    private MainWindowViewModel _viewModel;
    private bool _chartReady;

    public MainWindow()
    {
        InitializeComponent();

        var indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        _chart = this.FindControl<NativeWebView>("Chart");
        _chart!.NavigationCompleted += (_, e) =>
        {
            _chartReady = e.IsSuccess;
            if (_chartReady)
            {
                FlushPendingCandles();
            }
        };

        _chart.WebMessageReceived += (_, e) =>
        {
            if (e.Body != null && e.Body.Contains("closeTrade"))
            {
                Dispatcher.UIThread.Post(() => _ = _viewModel?.CloseActiveTrade());
            }
        };

        _chart.Source = new Uri(indexPath);

        DataContextChanged += (_, _) =>
        {
            if (_viewModel != null)
            {
                _viewModel.OnBarPush -= PushCandle;
                _viewModel.OnOrderPriceLine -= SetOrderPriceLine;
                _viewModel.OnMagnetLevels -= SetMagnetLevels;
            }

            _viewModel = DataContext as MainWindowViewModel;
            if (_viewModel != null)
            {
                _viewModel.OnBarPush += PushCandle;
                _viewModel.OnOrderPriceLine += SetOrderPriceLine;
                _viewModel.OnMagnetLevels += SetMagnetLevels;
            }
        };
    }

    private void PushCandle(Bar bar, string color)
    {
        if (!_chartReady)
        {
            lock (_pendingCandles)
            {
                _pendingCandles.Add((bar, color));
            }
            return;
        }

        var script = $"window.pushCandle({bar.ChartTime}, {Price(bar.Open)}, {Price(bar.High)}, " +
                     $"{Price(bar.Low)}, {Price(bar.Close)}, '{color}')";
        Dispatcher.UIThread.Post(() => _ = InvokeScript(script));
    }

    private void SetOrderPriceLine(double price, string color, string title)
    {
        if (!_chartReady)
        {
            _pendingOrderLine = (price, color, title);
            return;
        }

        var script = $"window.setOrderLine({Price(price)}, '{color}', '{title}')";
        Dispatcher.UIThread.Post(() => _ = InvokeScript(script));
    }

    private void SetMagnetLevels(IReadOnlyList<MagnetLevel> levels)
    {
        if (!_chartReady)
        {
            _pendingMagnetLevels = levels;
            return;
        }

        var lineDtos = levels
            .Where(l => l.Percentage > 20.0 && l.Strike > 0)
            .Select(l => new
            {
                price = l.Strike,
                percentage = l.Percentage,
                title = $"Magnet {l.Percentage:F1}%",
                color = l.Percentage >= 50.0 ? "#ba68c8" : "#9575cd"
            })
            .ToList();

        var json = JsonSerializer.Serialize(lineDtos);
        var script = $"window.setMagnetLines({json})";
        Dispatcher.UIThread.Post(() => _ = InvokeScript(script));
    }

    private void FlushPendingCandles()
    {
        List<(Bar Bar, string Color)> candles;
        lock (_pendingCandles)
        {
            candles = new List<(Bar Bar, string Color)>(_pendingCandles);
            _pendingCandles.Clear();
        }

        foreach (var (bar, color) in candles)
        {
            PushCandle(bar, color);
        }

        if (_pendingOrderLine.HasValue)
        {
            SetOrderPriceLine(_pendingOrderLine.Value.Price, _pendingOrderLine.Value.Color, _pendingOrderLine.Value.Title);
            _pendingOrderLine = null;
        }

        if (_pendingMagnetLevels != null)
        {
            SetMagnetLevels(_pendingMagnetLevels);
            _pendingMagnetLevels = null;
        }
    }

    private Task<string> InvokeScript(string script)
    {
        return _chart.InvokeScript(script);
    }

    private static string Price(double price)
    {
        return price.ToString("F2", CultureInfo.InvariantCulture);
    }
}
