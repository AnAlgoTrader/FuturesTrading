using System;
using Avalonia.Threading;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace QQQTrader.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    public event Action<string>? OnInvokeScript;

    public ReactiveCommand<RxVoid, RxVoid> OnButtonClick { get; }

    private readonly DispatcherTimer _timer;

    public MainWindowViewModel()
    {
        OnButtonClick = ReactiveCommand.Create(OnButtonClicked);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => TimerTick();
        _timer.Start();
    }

    private void TimerTick()
    {
        //OnInvokeScript?.Invoke($"window.notifyUI('this is a message from the MainWindowViewModel ({_count})')");
    }

    private void OnButtonClicked()
    {
        Console.WriteLine("Button clicked");
    }
}