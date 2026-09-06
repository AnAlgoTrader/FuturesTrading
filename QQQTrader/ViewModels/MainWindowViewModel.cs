using System;

namespace QQQTrader.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    public event Action<string>? OnInvokeScript;

    public void ShowInitialMessage()
    {
        OnInvokeScript?.Invoke("window.notifyUI('this is a message from the MainWindowViewModel')");
    }
}