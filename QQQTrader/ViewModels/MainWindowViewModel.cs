using System;
using Avalonia.Threading;

namespace QQQTrader.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    public event Action<string>? OnInvokeScript;

    private readonly DispatcherTimer _timer;
    private int _count;

    public MainWindowViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => PublishMessage();
        _timer.Start();
    }

    private void PublishMessage()
    {
        _count++;
        OnInvokeScript?.Invoke($"window.notifyUI('this is a message from the MainWindowViewModel ({_count})')");
    }
}