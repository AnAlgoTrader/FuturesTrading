using System;
using System.Threading.Tasks;
using Ib.Csharp.Api.Client.Implementation;
using Ib.Csharp.Api.Client.Implementation.Helper;
using Ib.Csharp.Api.Client.Implementation.Model;
using IBApi;
using QQQTrader.Helper;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace QQQTrader.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    public event Action<string>? OnInvokeScript;
    public ReactiveCommand<RxVoid, RxVoid> OnButtonClick { get; }
    private readonly IbClient _client = new();
    private ContractDetails? _qqqContract;

    public MainWindowViewModel()
    {
        OnButtonClick = ReactiveCommand.Create(OnButtonClicked);
        _ = InitAndSubscribe();
    }

    private async Task InitAndSubscribe()
    {
        _client.OnNotification += Console.WriteLine;
        _client.Start("127.0.0.1", 4001, 0);
        _qqqContract = await ContractHelper.GetQQQContract(_client);
        await DisplayHistoricalData();
    }

    private async Task DisplayHistoricalData()
    {
        var historicalData = await _client.GetHistoricalData(1105, _qqqContract!.Contract, $"1 {nameof(Duration.D)}", $"5 {nameof(BarSize.secs)}", "TRADES", 600000);
        foreach (var item in historicalData)
        {
            OnInvokeScript?.Invoke($"window.updateData({item.Time}, {item.Open}, {item.High}, {item.Low}, {item.Close}, {item.WAP})");
            Console.WriteLine(item.WAP);
        }
    }

    private void OnButtonClicked()
    {
        Console.WriteLine("Button clicked");
    }
}