using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FootPrintTrader.Services.Logging;
using FootPrintTrader.UI.Models;
using Ib.Csharp.Api.Client.Implementation;
using Ib.Csharp.Api.Client.Implementation.Helper;
using Ib.Csharp.Api.Client.Implementation.Model;
using IBApi;

namespace FootPrintTrader.Services.Options;

public class OptionsService
{
    private const int FirstSpreadRequestId = 2100;

    private static readonly Regex IbMessagePattern = new(
        @"Id:(-?\d+) - Code:(\d+) - (?<message>.*)$",
        RegexOptions.Compiled);

    private readonly Logger _logger;
    private readonly object _sync = new();

    private int _optionsFetchInProgress;
    private volatile OptionsChainUpdate _lastOptionsChain;

    private IbClient _client;

    // Cboe chain identity used to build canonical IB option contracts.
    private string _underlyingSymbol = "SPX";
    private string _tradingClass = "SPXW";
    private string _expiration = "";

    // Currently staged spread and its live subscription.
    private OptionsSpreadCommand _activeSpread;
    private int _shortRequestId = -1;
    private int _longRequestId = -1;
    private int _nextRequestId = FirstSpreadRequestId;
    private double _lastShortBid;
    private double _lastShortAsk;
    private double _lastLongBid;
    private double _lastLongAsk;
    private string _subscriptionState = "";

    public OptionsChainUpdate LastOptionsChain => _lastOptionsChain;

    public event Action<OptionsChainUpdate> OnOptionsChainUpdated;
    public event Action<string> OnOptionsError;
    public event Action<OptionsSpreadQuoteUpdate> OnSpreadQuoteUpdated;
    public event Action<OptionsSpreadStatusUpdate> OnSpreadStatusChanged;

    public OptionsService(Logger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Shares the already-connected IB client used for futures market data.
    /// Must be called before a spread is staged.
    /// </summary>
    public void AttachClient(IbClient client)
    {
        if (client == null || ReferenceEquals(_client, client)) return;

        if (_client != null)
        {
            _client.OnMarketDataPriceUpdate -= HandleMarketDataPriceUpdate;
            _client.OnNotification -= HandleIbNotification;
        }

        _client = client;
        _client.OnMarketDataPriceUpdate += HandleMarketDataPriceUpdate;
        _client.OnNotification += HandleIbNotification;
    }

    public async Task FetchOptionsChainAsync()
    {
        if (Interlocked.Exchange(ref _optionsFetchInProgress, 1) == 1) return;

        try
        {
            var chain = await CboeHelper.GetTodaySpxwChain();
            if (chain == null)
            {
                const string message = "No SPXW chain found for today.";
                _logger?.WriteLine($"[OPTIONS] {message}");
                OnOptionsError?.Invoke(message);
                return;
            }

            var update = ConvertChain(chain, DateTime.Now);

            lock (_sync)
            {
                _tradingClass = string.IsNullOrWhiteSpace(chain.Symbol) ? "SPXW" : chain.Symbol;
                _underlyingSymbol = string.Equals(_tradingClass, "SPXW", StringComparison.OrdinalIgnoreCase) ? "SPX" : _tradingClass;
                _expiration = chain.ExpirationDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            }

            _lastOptionsChain = update;
            OnOptionsChainUpdated?.Invoke(update);
            _logger?.WriteLine($"[OPTIONS] {update.Symbol} {update.ExpirationDate} - {update.Rows.Count} strikes @ {update.UnderlyingPrice:F2}");
        }
        catch (Exception ex)
        {
            _logger?.WriteLine($"[OPTIONS] Fetch failed: {ex}");
            OnOptionsError?.Invoke("Options chain unavailable. Check cboeData.json.");
        }
        finally
        {
            Interlocked.Exchange(ref _optionsFetchInProgress, 0);
        }
    }

    /// <summary>
    /// Starts real-time bid/ask subscriptions for the two legs of a staged spread.
    /// A previously staged spread is unsubscribed first.
    /// </summary>
    public void StageSpread(OptionsSpreadCommand command)
    {
        if (command == null) return;

        var right = NormalizeRight(command.ShortRight, command.Strategy);
        var longRight = NormalizeRight(command.LongRight, command.Strategy);

        if (command.ShortStrike <= 0 || command.LongStrike <= 0 || right == null || longRight == null)
        {
            PublishStatus("Error", "Invalid spread legs.", command);
            return;
        }

        if (_client == null)
        {
            PublishStatus("Error", "IB client is not connected yet.", command);
            return;
        }

        ContractDetails shortContract = null;
        ContractDetails longContract = null;
        int shortRequestId = -1;
        int longRequestId = -1;
        int previousShortRequestId = -1;
        int previousLongRequestId = -1;
        OptionsSpreadCommand activeSpread = null;

        lock (_sync)
        {
            if (IsSameSpread(_activeSpread, command, right, longRight) && _subscriptionState != "Error")
            {
                return;
            }

            if (!string.IsNullOrEmpty(_expiration))
            {
                shortContract = BuildOptionContract(command.ShortStrike, right);
                longContract = BuildOptionContract(command.LongStrike, longRight);

                previousShortRequestId = _shortRequestId;
                previousLongRequestId = _longRequestId;

                shortRequestId = _nextRequestId;
                longRequestId = _nextRequestId + 1;
                _nextRequestId += 2;

                _activeSpread = new OptionsSpreadCommand
                {
                    Action = "stageSpread",
                    Strategy = command.Strategy ?? "",
                    ShortStrike = command.ShortStrike,
                    LongStrike = command.LongStrike,
                    ShortRight = right,
                    LongRight = longRight
                };
                activeSpread = _activeSpread;
                _shortRequestId = shortRequestId;
                _longRequestId = longRequestId;
                _lastShortBid = _lastShortAsk = _lastLongBid = _lastLongAsk = 0;
                _subscriptionState = "Subscribing";
            }
        }

        if (shortContract == null)
        {
            PublishStatus("Error", "Options chain is not loaded yet.", command);
            return;
        }

        if (previousShortRequestId >= 0)
        {
            TryUnsubscribe(previousShortRequestId);
            TryUnsubscribe(previousLongRequestId);
        }

        _logger?.WriteLine(
            $"[OPTIONS] Spread subscribe {command.Strategy} {command.ShortStrike} {right} / {command.LongStrike} {longRight} " +
            $"reqIds={shortRequestId}/{longRequestId}");

        PublishStatus("Subscribing", "Requesting live quotes...", activeSpread);

        try
        {
            _client.SubscribeToMarketData(shortRequestId, shortContract);
            _client.SubscribeToMarketData(longRequestId, longContract);
        }
        catch (Exception ex)
        {
            _logger?.WriteLine($"[OPTIONS] Spread subscribe failed: {ex}");
            PublishStatus("Error", "Could not subscribe to option quotes.", activeSpread);
        }
    }

    /// <summary>
    /// Cancels the live subscriptions for the currently staged spread.
    /// </summary>
    public void ClearSpread()
    {
        int shortRequestId;
        int longRequestId;
        OptionsSpreadCommand cleared;

        lock (_sync)
        {
            shortRequestId = _shortRequestId;
            longRequestId = _longRequestId;
            cleared = _activeSpread;

            _activeSpread = null;
            _shortRequestId = -1;
            _longRequestId = -1;
            _lastShortBid = _lastShortAsk = _lastLongBid = _lastLongAsk = 0;
            _subscriptionState = "";
        }

        if (shortRequestId >= 0 && _client != null)
        {
            TryUnsubscribe(shortRequestId);
            TryUnsubscribe(longRequestId);
        }

        if (cleared != null)
        {
            _logger?.WriteLine($"[OPTIONS] Spread unsubscribe {cleared.Strategy} {cleared.ShortStrike}/{cleared.LongStrike}");
            PublishStatus("Cleared", "", cleared);
        }
    }

    private void TryUnsubscribe(int requestId)
    {
        try
        {
            _client?.UnsubscribeFromMarketData(requestId);
        }
        catch (Exception ex)
        {
            _logger?.WriteLine($"[OPTIONS] Unsubscribe {requestId} failed: {ex.Message}");
        }
    }

    private void HandleMarketDataPriceUpdate(Dictionary<int, PriceUpdate> updates)
    {
        OptionsSpreadQuoteUpdate quote = null;
        OptionsSpreadStatusUpdate status = null;

        lock (_sync)
        {
            if (_activeSpread == null || _shortRequestId < 0 || updates == null) return;

            var hasShort = updates.TryGetValue(_shortRequestId, out var shortPrice);
            var hasLong = updates.TryGetValue(_longRequestId, out var longPrice);
            if (!hasShort && !hasLong) return;

            var shortBid = hasShort ? shortPrice.BidPrice : _lastShortBid;
            var shortAsk = hasShort ? shortPrice.AskPrice : _lastShortAsk;
            var longBid = hasLong ? longPrice.BidPrice : _lastLongBid;
            var longAsk = hasLong ? longPrice.AskPrice : _lastLongAsk;

            if (shortBid <= 0 && shortAsk <= 0 && longBid <= 0 && longAsk <= 0) return;

            if (shortBid == _lastShortBid && shortAsk == _lastShortAsk &&
                longBid == _lastLongBid && longAsk == _lastLongAsk)
            {
                return;
            }

            _lastShortBid = shortBid;
            _lastShortAsk = shortAsk;
            _lastLongBid = longBid;
            _lastLongAsk = longAsk;

            quote = new OptionsSpreadQuoteUpdate
            {
                Strategy = _activeSpread.Strategy,
                ShortStrike = _activeSpread.ShortStrike,
                LongStrike = _activeSpread.LongStrike,
                ShortBid = shortBid,
                ShortAsk = shortAsk,
                LongBid = longBid,
                LongAsk = longAsk,
                UpdatedAt = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            };

            if (_subscriptionState == "Subscribing" &&
                (shortBid > 0 || shortAsk > 0) &&
                (longBid > 0 || longAsk > 0))
            {
                _subscriptionState = "Live";
                status = BuildStatus("Live", "Live option quotes.");
            }
        }

        if (quote != null) OnSpreadQuoteUpdated?.Invoke(quote);
        if (status != null) OnSpreadStatusChanged?.Invoke(status);
    }

    private void HandleIbNotification(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        var match = IbMessagePattern.Match(message);
        if (!match.Success) return;

        if (!int.TryParse(match.Groups[1].Value, out var requestId)) return;
        if (!int.TryParse(match.Groups[2].Value, out var code)) return;

        string state;
        string text;

        switch (code)
        {
            case 354:
            case 10167:
            case 10089:
            case 10168:
                state = "Delayed";
                text = "No real-time OPRA subscription; delayed quotes.";
                break;
            case 200:
                state = "Error";
                text = "IB rejected the option contract.";
                break;
            case 502:
            case 504:
                state = "Error";
                text = "Lost connection to TWS/Gateway.";
                break;
            case 10197:
                state = "Error";
                text = "No option market data available.";
                break;
            default:
                return;
        }

        OptionsSpreadCommand activeSpread;
        lock (_sync)
        {
            if (requestId != _shortRequestId && requestId != _longRequestId) return;

            _subscriptionState = state;
            activeSpread = _activeSpread;
        }

        _logger?.WriteLine($"[OPTIONS] IB {code} for request {requestId}: {match.Groups["message"].Value}");
        PublishStatus(state, text, activeSpread);
    }

    private void PublishStatus(string state, string message, OptionsSpreadCommand spread)
    {
        OnSpreadStatusChanged?.Invoke(new OptionsSpreadStatusUpdate
        {
            State = state,
            Message = message,
            Strategy = spread?.Strategy ?? "",
            ShortStrike = spread?.ShortStrike ?? 0,
            LongStrike = spread?.LongStrike ?? 0
        });
    }

    private OptionsSpreadStatusUpdate BuildStatus(string state, string message)
    {
        return new OptionsSpreadStatusUpdate
        {
            State = state,
            Message = message,
            Strategy = _activeSpread?.Strategy ?? "",
            ShortStrike = _activeSpread?.ShortStrike ?? 0,
            LongStrike = _activeSpread?.LongStrike ?? 0
        };
    }

    private ContractDetails BuildOptionContract(double strike, string right)
    {
        return new ContractDetails
        {
            Contract = new Contract
            {
                Symbol = _underlyingSymbol,
                SecType = "OPT",
                LastTradeDateOrContractMonth = _expiration,
                Strike = strike,
                Right = right,
                Multiplier = "100",
                Exchange = "SMART",
                Currency = "USD",
                TradingClass = _tradingClass
            }
        };
    }

    private static bool IsSameSpread(
        OptionsSpreadCommand active,
        OptionsSpreadCommand requested,
        string right,
        string longRight)
    {
        return active != null
               && Math.Abs(active.ShortStrike - requested.ShortStrike) < 0.001
               && Math.Abs(active.LongStrike - requested.LongStrike) < 0.001
               && string.Equals(active.ShortRight, right, StringComparison.OrdinalIgnoreCase)
               && string.Equals(active.LongRight, longRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRight(string right, string strategy)
    {
        if (string.Equals(right, "C", StringComparison.OrdinalIgnoreCase)) return "C";
        if (string.Equals(right, "P", StringComparison.OrdinalIgnoreCase)) return "P";

        if (strategy != null && strategy.Contains("CALL", StringComparison.OrdinalIgnoreCase)) return "C";
        if (strategy != null && strategy.Contains("PUT", StringComparison.OrdinalIgnoreCase)) return "P";

        return null;
    }

    public static OptionsChainUpdate ConvertChain(CboeChain chain, DateTime timestamp)
    {
        var source = chain.Data ?? new List<OptionChainData>();
        var spot = source.Count > 0 ? source[0].UnderlyingPrice : 0;

        return new OptionsChainUpdate
        {
            Symbol = chain.Symbol ?? "",
            ExpirationDate = chain.ExpirationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            UnderlyingPrice = spot,
            UpdatedAt = timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            Rows = source
                .OrderByDescending(row => row.Strike)
                .Select(row => new OptionsChainRow
                {
                    Strike = row.Strike,
                    CallBid = row.CallBid,
                    CallAsk = row.CallAsk,
                    CallIv = row.CallIv,
                    CallDelta = row.CallDelta,
                    CallGamma = row.CallGamma,
                    CallOpenInterest = row.CallOpenInterest,
                    PutBid = row.PutBid,
                    PutAsk = row.PutAsk,
                    PutIv = row.PutIv,
                    PutDelta = row.PutDelta,
                    PutGamma = row.PutGamma,
                    PutOpenInterest = row.PutOpenInterest,
                    Gex = ComputeGex(row, spot)
                })
                .ToList()
        };
    }

    public static double ComputeGex(OptionChainData row, double spot)
    {
        if (spot <= 0) return 0;

        const double contractMultiplier = 100.0;
        const double onePercent = 0.01;

        return (row.CallGamma * row.CallOpenInterest - row.PutGamma * row.PutOpenInterest)
               * contractMultiplier * spot * spot * onePercent;
    }
}
