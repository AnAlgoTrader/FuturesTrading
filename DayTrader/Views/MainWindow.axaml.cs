using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using DayTrader.ViewModels;

namespace DayTrader.Views;

public partial class MainWindow : Window
{
    private readonly NativeWebView _chart;
    private string? _pendingScript;
    private bool _chartReady;
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        var indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        _chart = this.FindControl<NativeWebView>("Chart")!;

        _chart.NavigationCompleted += (_, e) =>
        {
            _chartReady = e.IsSuccess;
            if (_chartReady && _pendingScript is { } script)
            {
                _pendingScript = null;
                Dispatcher.UIThread.Post(() => _ = _chart.InvokeScript(script));
            }
        };

        _chart.WebMessageReceived += (_, e) =>
        {
            var body = e.Body?.ToString();
            if (body == null) return;

            if (body.StartsWith("jsError: ", StringComparison.Ordinal))
            {
                Console.WriteLine($"[JS] {body["jsError: ".Length..]}");
                return;
            }

            if (body.Contains("closeTrade"))
            {
                Dispatcher.UIThread.Post(() => _ = _viewModel?.CloseActiveTrade());
            }
        };

        _chart.Source = new Uri(indexPath);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainWindowViewModel vm) return;
            _viewModel = vm;
            vm.OnInvokeScript += script =>
            {
                if (_chartReady)
                {
                    Dispatcher.UIThread.Post(() => _ = _chart.InvokeScript(script));
                }
                else
                {
                    _pendingScript = script;
                }
            };
            vm.OnOrderPriceLine += (price, color, title, chartPrice) =>
            {
                var script = $"window.setOrderLine({Price(price)}, '{color}', '{EscapeTitle(title)}', {Price(chartPrice)})";
                InvokeInChart(script);
            };
        };
    }

    private void InvokeInChart(string script)
    {
        if (_chartReady)
        {
            Dispatcher.UIThread.Post(() => _ = _chart.InvokeScript(script));
        }
        else
        {
            _pendingScript = script;
        }
    }

    private static string Price(double price) => price.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

    private static string EscapeTitle(string title) => title.Replace("'", "\\'");
}
