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
    private MainWindowViewModel _viewModel;
    private bool _chartReady;

    public MainWindow()
    {
        InitializeComponent();

        var indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        _chart = this.FindControl<NativeWebView>("Chart");
        _chart!.NavigationCompleted += (_, e) => _chartReady = e.IsSuccess;
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

    private void PushCandle(Bar bar)
    {
        if (!_chartReady)
            return;

        var script = $"window.pushCandle({bar.ChartTime}, {Price(bar.Open)}, {Price(bar.High)}, " +
                     $"{Price(bar.Low)}, {Price(bar.Close)})";
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
