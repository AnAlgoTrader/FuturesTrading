using System;
using System.Drawing;
using System.Text.Json;
using FootPrintTrader.UI.Models;
using FootPrintTrader.UI.Rendering;
using Photino.NET;

namespace FootPrintTrader.UI;

public class WindowManager
{
    private PhotinoWindow _futuresWindow;
    private PhotinoWindow _optionsWindow;

    public event Action OnApplicationReady;
    public event Action OnBuyRequested;
    public event Action OnSellRequested;
    public event Action OnCloseTradeRequested;
    public event Action OnOptionsReady;

    public event Action OnFuturesWindowClosing;
    public event Action OnOptionsWindowClosing;

    public void InitializeFuturesWindow(string marketDisplayName)
    {
        _futuresWindow = new PhotinoWindow
        {
            LogVerbosity = 0,
            MinSize = new Point(800, 600),
            Size = new Size(1280, 800)
        }
        .SetTitle($"Footprint Trader - {marketDisplayName}")
        .SetUseOsDefaultSize(false)
        .SetMaximized(true)
        .RegisterWebMessageReceivedHandler(ReceiveFuturesMessage)
        .Load("wwwroot/futures.html");

        _futuresWindow.WindowClosing += (_, _) =>
        {
            OnFuturesWindowClosing?.Invoke();
            _optionsWindow?.Close();
            return false;
        };
    }

    public void OpenOptionsWindow(string marketDisplayName)
    {
        if (_optionsWindow != null) return;

        var optionsWindow = new PhotinoWindow(_futuresWindow)
        {
            LogVerbosity = 0,
            MinSize = new Point(800, 600),
            Size = new Size(1280, 800)
        };

        _optionsWindow = optionsWindow
            .SetTitle($"Options Trader - {marketDisplayName}")
            .SetUseOsDefaultSize(false)
            .SetMaximized(true)
            .RegisterWebMessageReceivedHandler(ReceiveOptionsMessage)
            .Load("wwwroot/options.html");

        _optionsWindow.WindowClosing += (_, _) =>
        {
            OnOptionsWindowClosing?.Invoke();
            _optionsWindow = null;
            return false;
        };

        optionsWindow.WaitForClose();
    }

    public void WaitForClose()
    {
        _futuresWindow?.WaitForClose();
    }

    public void SendFuturesUpdate(string type, object data)
    {
        SendUpdate(_futuresWindow, type, data);
    }

    public void SendOptionsUpdate(string type, object data)
    {
        SendUpdate(_optionsWindow, type, data);
    }

    private static void SendUpdate(PhotinoWindow window, string type, object data)
    {
        if (window == null) return;

        var message = new UpdateMessage
        {
            Type = type,
            Data = JsonSerializer.SerializeToElement(data, FootprintRenderBuilder.JsonOptions)
        };

        try
        {
            window.SendWebMessage(JsonSerializer.Serialize(message));
        }
        catch
        {
            // Suppress error if window was closed or IPC disconnected
        }
    }

    private void ReceiveFuturesMessage(object sender, string message)
    {
        switch (message)
        {
            case "applicationReady":
                OnApplicationReady?.Invoke();
                break;
            case "buy":
                OnBuyRequested?.Invoke();
                break;
            case "sell":
                OnSellRequested?.Invoke();
                break;
            case "closeTrade":
                OnCloseTradeRequested?.Invoke();
                break;
        }
    }

    private void ReceiveOptionsMessage(object sender, string message)
    {
        if (message == "optionsReady")
        {
            OnOptionsReady?.Invoke();
        }
    }
}
