using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Common;
using UI.ViewModels;

namespace UI.Views;

public partial class MainWindow : Window
{
    private readonly NativeWebView _chart;
    private MainWindowViewModel _viewModel;
    private bool _chartReady;

    public MainWindow()
    {
        InitializeComponent();

        var indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        _chart = this.FindControl<NativeWebView>("Chart");
        _chart.NavigationCompleted += (_, e) => _chartReady = e.IsSuccess;
        _chart.Source = new Uri(indexPath);

        DataContextChanged += (_, _) =>
        {
            if (_viewModel != null)
                _viewModel.OnCandleUpdated -= PushCandle;
            _viewModel = DataContext as MainWindowViewModel;
            if (_viewModel != null)
                _viewModel.OnCandleUpdated += PushCandle;
        };
    }

    private void PushCandle(Candle candle)
    {
        if (!_chartReady)
            return;

        var time = new DateTimeOffset(candle.Timestamp).ToUnixTimeSeconds();
        var script = $"window.pushCandle({time}, {Price(candle.Open)}, {Price(candle.High)}, " +
                     $"{Price(candle.Low)}, {Price(candle.Close)})";
        Dispatcher.UIThread.Post(() => _ = InvokeScript(script));
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
