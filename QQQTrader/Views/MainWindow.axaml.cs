using System;
using System.IO;
using Avalonia.Controls;
using QQQTrader.ViewModels;

namespace QQQTrader.Views;

public partial class MainWindow : Window
{
    private readonly NativeWebView _chart;
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        var indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        _chart = this.FindControl<NativeWebView>("Chart");
        _chart!.Source = new Uri(indexPath);
        _viewModel = DataContext as MainWindowViewModel;
        _viewModel!.OnInvokeScript += OnInvokeScript;
    }

    private void OnInvokeScript(string script)
    {
        Dispatcher.UIThread.Post(() => _ = _chart.InvokeScript(script));
    }
}