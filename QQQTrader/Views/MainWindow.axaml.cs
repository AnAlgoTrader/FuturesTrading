using System;
using System.IO;
using Avalonia.Controls;

namespace QQQTrader.Views;

public partial class MainWindow : Window
{
    private readonly NativeWebView _chart;
    
    public MainWindow()
    {
        InitializeComponent();
        var indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        _chart = this.FindControl<NativeWebView>("Chart");
        _chart.Source = new Uri(indexPath);
    }
}