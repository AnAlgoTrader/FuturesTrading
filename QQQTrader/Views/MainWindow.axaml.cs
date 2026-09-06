using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Threading;
using QQQTrader.ViewModels;

namespace QQQTrader.Views;

public partial class MainWindow : Window
{
    private readonly NativeWebView _chart;
    private string? _pendingScript;
    private bool _chartReady;

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

        _chart.Source = new Uri(indexPath);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainWindowViewModel vm) return;
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
        };
    }
}