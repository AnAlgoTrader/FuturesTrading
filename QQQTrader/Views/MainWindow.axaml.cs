using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Threading;
using QQQTrader.ViewModels;

namespace QQQTrader.Views;

public partial class MainWindow : Window
{
    private readonly NativeWebView _chart;

    public MainWindow()
    {
        InitializeComponent();
        var indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        _chart = this.FindControl<NativeWebView>("Chart")!;
        _chart.Source = new Uri(indexPath);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.OnInvokeScript += script =>
                    Dispatcher.UIThread.Post(() => _ = _chart.InvokeScript(script));
            }
        };
    }
}