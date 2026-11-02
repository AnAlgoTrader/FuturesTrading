using System;
using System.Globalization;
using System.Threading.Tasks;
using FootPrintTrader.Configuration;
using FootPrintTrader.Domain.Footprint;
using FootPrintTrader.Services.Logging;
using Ib.Csharp.Api.Client.Implementation;
using Ib.Csharp.Api.Client.Implementation.Model;
using IBApi;

namespace FootPrintTrader.Services.MarketData;

public class MarketDataService
{
    private const int MarketDataRequestId = 1108;
    private const int PriceTickRequestId = 1109;
    private const int TradeTickRequestId = 1110;

    private readonly MarketProfile _market;
    private readonly FootprintClassifier _classifier;
    private readonly Logger _logger;
    private readonly IbClient _client = new();

    public IbClient Client => _client;
    public ContractDetails DataContract { get; private set; }
    public ContractDetails TradeContract { get; private set; }

    public double Bid { get; private set; }
    public double Ask { get; private set; }
    public double CurrentPrice { get; set; }

    public double TradeMultiplier =>
        double.TryParse(TradeContract?.Contract.Multiplier, NumberStyles.Float, CultureInfo.InvariantCulture, out var m)
            ? m
            : _market.TradeMultiplier;

    public event Action<double, double> OnQuoteUpdated;
    public event Action<double> OnPriceUpdated;
    public event Action<OrderStatus> OnOrderStatusUpdated;

    public MarketDataService(MarketProfile market, FootprintClassifier classifier, Logger logger)
    {
        _market = market;
        _classifier = classifier;
        _logger = logger;
    }

    public async Task StartAsync(string host = "127.0.0.1", int port = 4001, int clientId = 0)
    {
        _client.OnNotification += msg => _logger?.WriteLine($"[IB] {msg}");
        _client.OnOrderStatusUpdate += status => OnOrderStatusUpdated?.Invoke(status);

        _client.OnMarketDataPriceTickByTickUpdate += price =>
        {
            _classifier.OnQuote(price.Time, price.Bid, price.Ask);
            if (price.Bid > 0 || price.Ask > 0)
            {
                if (price.Bid > 0) Bid = price.Bid;
                if (price.Ask > 0) Ask = price.Ask;
                CurrentPrice = Bid > 0 && Ask > 0 ? (Bid + Ask) / 2.0 : (Bid > 0 ? Bid : Ask);
                OnQuoteUpdated?.Invoke(Bid, Ask);
                OnPriceUpdated?.Invoke(CurrentPrice);
            }
        };

        _client.OnMarketDataPriceUpdate += updates =>
        {
            foreach (var (key, marketPrice) in updates)
            {
                if (key != MarketDataRequestId) continue;
                if (marketPrice.BidPrice > 0) Bid = marketPrice.BidPrice;
                if (marketPrice.AskPrice > 0) Ask = marketPrice.AskPrice;
                CurrentPrice = Bid > 0 && Ask > 0 ? (Bid + Ask) / 2.0 : (Bid > 0 ? Bid : Ask);
                OnQuoteUpdated?.Invoke(Bid, Ask);
                OnPriceUpdated?.Invoke(CurrentPrice);
            }
        };

        _client.OnMarketDataTradeTickByTickUpdate += trade =>
        {
            _classifier.OnTrade(trade.Time, trade.Price, trade.Size);
            if (trade.Price > 0)
            {
                CurrentPrice = trade.Price;
                OnPriceUpdated?.Invoke(CurrentPrice);
                if (Bid <= 0 && Ask <= 0)
                {
                    Bid = CurrentPrice - _market.TickSize * 0.5;
                    Ask = CurrentPrice + _market.TickSize * 0.5;
                    OnQuoteUpdated?.Invoke(Bid, Ask);
                }
            }
        };

        _client.Start(host, port, clientId);

        DataContract = await ContractResolver.ResolveContractAsync(_client, _market, _market.DataSymbol);
        TradeContract = await ContractResolver.ResolveContractAsync(_client, _market, _market.TradeSymbol);

        _client.SubscribeToMarketData(MarketDataRequestId, DataContract);
        _client.SubscribeToPriceTickByTick(PriceTickRequestId, DataContract.Contract);
        _client.SubscribeToTradesTickByTick(TradeTickRequestId, DataContract.Contract);
    }

    public void UpdateSyntheticBidAsk(double price)
    {
        CurrentPrice = price;
        Bid = CurrentPrice - _market.TickSize * 0.5;
        Ask = CurrentPrice + _market.TickSize * 0.5;
        OnQuoteUpdated?.Invoke(Bid, Ask);
        OnPriceUpdated?.Invoke(CurrentPrice);
    }
}
