using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using DayTrader.Helper;
using DayTrader.ViewModels;

namespace DayTrader.Views;

public partial class MainWindow : Window
{
    private readonly NativeWebView _chart;
    private readonly System.Collections.Generic.List<(Bar Bar, string Color)> _pendingCandles = new();
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
        _chart.Source = new Uri(indexPath);

        DataContextChanged += (_, _) =>
        {
            if (_viewModel != null)
                _viewModel.OnBarPush -= PushCandle;

            _viewModel = DataContext as MainWindowViewModel;
            if (_viewModel != null)
                _viewModel.OnBarPush += PushCandle;
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

    private void FlushPendingCandles()
    {
        System.Collections.Generic.List<(Bar Bar, string Color)> candles;
        lock (_pendingCandles)
        {
            candles = new System.Collections.Generic.List<(Bar Bar, string Color)>(_pendingCandles);
            _pendingCandles.Clear();
        }

        foreach (var (bar, color) in candles)
        {
            PushCandle(bar, color);
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
