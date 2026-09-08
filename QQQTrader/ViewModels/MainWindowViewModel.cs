using System;
using System.Threading.Tasks;
using Ib.Csharp.Api.Client.Implementation;
using Ib.Csharp.Api.Client.Implementation.Model;
using IBApi;
using QQQTrader.Helper;
using QQQTrader.Model;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace QQQTrader.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    public event Action<string>? OnInvokeScript;
    public ReactiveCommand<RxVoid, RxVoid> OnEnterButtonClick { get; }
    private readonly IbClient _client = new();
    private ContractDetails? _qqqContract;
    private readonly VwapCalculator _vwapCalculator;
    private readonly CandleAggregator _candleAggregator;
    public AccountInfo AccountInfo { get; } = new();

    public MainWindowViewModel()
    {
        _vwapCalculator = new VwapCalculator();
        _candleAggregator = new CandleAggregator();
        _candleAggregator.OnCandleUpdate += (time, open, high, low, close, vwap) =>
            OnInvokeScript?.Invoke($"window.updateData({time}, {open}, {high}, {low}, {close}, {vwap})");
        OnEnterButtonClick = ReactiveCommand.Create(OnButtonClicked);
        _ = InitAndSubscribe();
    }

    private async Task InitAndSubscribe()
    {
        _client.OnNotification += Console.WriteLine;
        _client.Start("127.0.0.1", 4001, 0);
        _client.OnAccountUpdate += accountUpdate =>
        {
            AccountInfo.UpdateOn = accountUpdate.UpdateOn;
            AccountInfo.AvailableFunds = accountUpdate.AvailableFunds;
            AccountInfo.CashEuro = accountUpdate.CashEuro;
            AccountInfo.CashUsd = accountUpdate.CashUsd;
            AccountInfo.BuyingPower = accountUpdate.BuyingPower;
        };
        _client.OnAccountListUpdate += _client.SubscribeToPortfolioUpdates;
        _client.OnRealTimeBarUpdate += _candleAggregator.AddBar;
        _qqqContract = await ContractHelper.GetQQQContract(_client);
        await DisplayHistoricalData();
        _client.SubscribeToRealTimeBars(1106, _qqqContract.Contract);
    }

    private async Task DisplayHistoricalData()
    {
        var historicalData = await _client.GetHistoricalData(1105, _qqqContract!.Contract, $"3 {nameof(Duration.D)}", $"1 {nameof(BarSize.min)}", "TRADES", 600000);

        foreach (var item in historicalData)
        {
            var time = long.Parse(item.Time);
            var vwap = _vwapCalculator.AddBar(time, (double)item.WAP, (double)item.Volume);
            OnInvokeScript?.Invoke($"window.updateData({item.Time}, {item.Open}, {item.High}, {item.Low}, {item.Close}, {vwap})");
        }
    }

    private void OnButtonClicked()
    {
        var order = new Order
        {
            OrderId = _client.GetNextValidId(),
            Action = "BUY",
            TotalQuantity = 25,
            OrderType = "LMT",
            LmtPrice = 700, //TODO get price
            Transmit = true,
            Tif = "DAY"
        };
        _client.PlaceOrder(_qqqContract?.Contract, order);
    }
}