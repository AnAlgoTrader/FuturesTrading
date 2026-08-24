using System;
using System.IO;
using Avalonia.Controls;

namespace UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        this.FindControl<NativeWebView>("Chart")!.Source = new Uri(indexPath);
    }

    private void WebView_NavigationCompleted(object sender, WebViewNavigationCompletedEventArgs e)
    {
        Console.WriteLine($"[WebView] Navigation success: {e.IsSuccess} url: {e.Request}");
    }
}
