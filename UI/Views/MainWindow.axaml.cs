using Avalonia.Controls;

namespace UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void WebView_NavigationCompleted(object sender, WebViewNavigationCompletedEventArgs e)
    {
        
    }
}