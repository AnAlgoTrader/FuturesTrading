using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Threading;
using QQQTrader.ViewModels;

namespace QQQTrader.Views;

public partial class MainWindow : Window
{
    private readonly NativeWebView _chart;
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        var indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        _chart = this.FindControl<NativeWebView>("Chart")!;

        _chart.NavigationCompleted += (_, e) =>
        {
            if (e.IsSuccess)
            {
                _viewModel?.ShowInitialMessage();
            }
        };

        _chart.Source = new Uri(indexPath);

        DataContextChanged += (_, _) =>
        {
            _viewModel = DataContext as MainWindowViewModel;
            if (_viewModel is not null)
            {
                _viewModel.OnInvokeScript += script =>
                    Dispatcher.UIThread.Post(() => _ = _chart.InvokeScript(script));
            }
        };
    }
}