using System;
using System.Runtime.InteropServices.JavaScript;
using Avalonia.Threading;
using Ib.Csharp.Api.Client.Implementation.Helper;
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

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => TimerTick();
        _timer.Start();
    }

    private void TimerTick()
    {
        var time = DateHelper.ToTradingViewTime(DateTime.Now);
        OnInvokeScript?.Invoke($"window.updateData({time}, 750, 760, 740, 755, 0)");
    }

    private void OnButtonClicked()
    {
        Console.WriteLine("Button clicked");
    }
}