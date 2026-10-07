using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using IndicatorBuild;
using IndicatorBuild.Analytics;
using IndicatorBuild.Indicators;
using IndicatorBuild.Model;
using IndicatorBuild.Rendering;

var projectRoot = FindProjectRoot();
var csvPath = Path.Combine(projectRoot, "data", "historicalData.csv");
var libraryPath = Path.Combine(projectRoot, "assets", "js", "lightweight-charts.standalone.production.js");
var outputDir = Path.Combine(projectRoot, "output");
var outputPath = Path.Combine(outputDir, "index.html");

var pivot = IndicatorConfig.SwingPivotLookback;
var sweepWick = IndicatorConfig.SweepMinWickFraction;
var fvgWidth = IndicatorConfig.FvgMinWidthAtrFactor;
var fvgBodies = false;
var fvgFill = IndicatorConfig.FvgFillFraction;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--pivot" when i + 1 < args.Length: pivot = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--sweep" when i + 1 < args.Length: sweepWick = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--fvg" when i + 1 < args.Length: fvgWidth = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--fvgbodies": fvgBodies = true; break;
        case "--fvgfill" when i + 1 < args.Length: fvgFill = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
    }
}

var candles = HistoricalDataSource.LoadFromCsv(csvPath);
var vwap = new List<VwapPoint>(candles.Count);
var vwapCalculator = new VwapCalculator();
foreach (var candle in candles)
{
    var typicalPrice = (candle.High + candle.Low + candle.Close) / 3.0;
    vwap.Add(new VwapPoint(candle.Time, vwapCalculator.AddBar(candle.Time, typicalPrice, candle.Volume)));
}

var pipeline = new IndicatorPipeline(
    new SwingDetector(pivot, pivot),
    new MarketStructureEngine(),
    new LiquiditySweepDetector(sweepWick),
    new FvgDetector(fvgWidth, fvgBodies, fvgFill));
var analyses = pipeline.Run(candles);
var swingHighs = pipeline.Swings.Count(s => s.IsHigh);
var swingLows = pipeline.Swings.Count(s => !s.IsHigh);

var metrics = BacktestStats.Collect(candles, pipeline.Events, pipeline.Zones, pivot, sweepWick, fvgWidth);
Console.WriteLine($"Loaded {candles.Count} candles from {csvPath}");
Console.WriteLine($"Pipeline: {analyses.Count} bars analysed, {swingHighs} swing highs, {swingLows} swing lows.");
Console.WriteLine($"Params: pivot={pivot} sweepWick={sweepWick:0.00} fvgWidth={fvgWidth:0.00} fvgBodies={fvgBodies} fvgFill={fvgFill:0.00}");
Console.WriteLine("M2 quality metrics:");
Console.WriteLine(metrics);
Console.WriteLine($"Structure totals: {pipeline.Events.Count(e => e.Type == Concept.BOS)} BOS, "
    + $"{pipeline.Events.Count(e => e.Type == Concept.ChoCh)} CHoCH, "
    + $"{pipeline.Events.Count(e => e.Type == Concept.LiquiditySweep)} sweep events, "
    + $"{pipeline.Zones.Count(z => z.Kind == ZoneKind.FairValueGap)} FVG zones.");

var model = new ChartModelBuilder().Build(candles, vwap, analyses, pipeline.Zones);
var html = ChartHtmlGenerator.Build(model, libraryPath);

Directory.CreateDirectory(outputDir);
File.WriteAllText(outputPath, html);

Console.WriteLine($"Loaded {candles.Count} candles from {csvPath}");
Console.WriteLine($"Pipeline: {analyses.Count} bars analysed, {swingHighs} swing highs, {swingLows} swing lows.");
Console.WriteLine($"Generated chart: {outputPath}");

static string FindProjectRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "IndicatorBuild.csproj")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return Directory.GetCurrentDirectory();
}
