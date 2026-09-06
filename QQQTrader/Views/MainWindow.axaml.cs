using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Threading;
using QQQTrader.ViewModels;

namespace QQQTrader.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        var chart = this.FindControl<NativeWebView>("Chart")!;
        chart.Source = new Uri(indexPath);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.OnInvokeScript += script =>
                    Dispatcher.UIThread.Post(() => _ = chart.InvokeScript(script));
            }
        };
    }
}