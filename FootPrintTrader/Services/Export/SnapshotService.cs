using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using FootPrintTrader.Configuration;
using FootPrintTrader.Domain.Footprint;
using FootPrintTrader.Services.Logging;
using FootPrintTrader.UI.Rendering;

namespace FootPrintTrader.Services.Export;

public class SnapshotService
{
    private readonly string _dataDirectory;
    private readonly MarketProfile _market;
    private readonly Logger _logger;
    private readonly FootprintAnalyzer _analyzer;
    private int _snapshotWritten;

    public SnapshotService(string dataDirectory, MarketProfile market, Logger logger, FootprintAnalyzer analyzer)
    {
        _dataDirectory = dataDirectory;
        _market = market;
        _logger = logger;
        _analyzer = analyzer;
    }

    public void WriteSnapshot(FootprintSnapshot snapshot, double currentPrice)
    {
        if (Interlocked.Exchange(ref _snapshotWritten, 1) == 1) return;

        try
        {
            if (snapshot == null) return;

            Directory.CreateDirectory(_dataDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var payload = FootprintRenderBuilder.Build(snapshot, _market.TickSize, currentPrice, _market, _analyzer);

            var jsonPath = Path.Combine(_dataDirectory, $"footprint-{stamp}.json");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(payload, FootprintRenderBuilder.JsonOptions));

            var dataPayload = FootprintDataExporter.Build(
                snapshot,
                _market.TickSize,
                _market.DataSymbol,
                _market.TradeSymbol,
                _market.TradesPerBar,
                DateTime.Now);
            var dataPath = Path.Combine(_dataDirectory, $"footprint-data-{stamp}.json");
            File.WriteAllText(dataPath, JsonSerializer.Serialize(dataPayload, FootprintRenderBuilder.JsonOptions));

            var htmlPath = Path.Combine(_dataDirectory, $"footprint-{stamp}.html");
            File.WriteAllText(htmlPath, FootprintHtmlExporter.Build(payload, DateTime.Now));

            _logger?.WriteLine($"[SNAPSHOT] {Path.GetFileName(jsonPath)} + {Path.GetFileName(dataPath)} + {Path.GetFileName(htmlPath)}");
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _snapshotWritten, 0);
            _logger?.WriteLine($"[SNAPSHOT] Write failed: {ex}");
        }
    }
}
