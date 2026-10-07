using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IndicatorBuild;
using IndicatorBuild.Indicators;
using IndicatorBuild.Model;
using IndicatorBuild.Rendering;

var projectRoot = FindProjectRoot();
var csvPath = Path.Combine(projectRoot, "data", "historicalData.csv");
var libraryPath = Path.Combine(projectRoot, "assets", "js", "lightweight-charts.standalone.production.js");
var outputDir = Path.Combine(projectRoot, "output");
var outputPath = Path.Combine(outputDir, "index.html");

var candles = HistoricalDataSource.LoadFromCsv(csvPath);
var vwap = new List<VwapPoint>(candles.Count);
var vwapCalculator = new VwapCalculator();
foreach (var candle in candles)
{
    var typicalPrice = (candle.High + candle.Low + candle.Close) / 3.0;
    vwap.Add(new VwapPoint(candle.Time, vwapCalculator.AddBar(candle.Time, typicalPrice, candle.Volume)));
}

var pipeline = new IndicatorPipeline(
    new SwingDetector(IndicatorConfig.SwingPivotLookback, IndicatorConfig.SwingPivotLookback),
    new MarketStructureEngine(),
    new LiquiditySweepDetector(IndicatorConfig.SweepMinWickFraction),
    new FvgDetector(IndicatorConfig.FvgMinWidthAtrFactor));
var analyses = pipeline.Run(candles);
var swingHighs = pipeline.Swings.Count(s => s.IsHigh);
var swingLows = pipeline.Swings.Count(s => !s.IsHigh);
var bosCount = pipeline.Events.Count(e => e.Type == Concept.BOS);
var chochCount = pipeline.Events.Count(e => e.Type == Concept.ChoCh);
var sweepCount = pipeline.Events.Count(e => e.Type == Concept.LiquiditySweep);
var fvgZones = pipeline.Zones.Count(z => z.Kind == ZoneKind.FairValueGap);

var model = new ChartModelBuilder().Build(candles, vwap, analyses, pipeline.Zones);
var html = ChartHtmlGenerator.Build(model, libraryPath);

Directory.CreateDirectory(outputDir);
File.WriteAllText(outputPath, html);

Console.WriteLine($"Loaded {candles.Count} candles from {csvPath}");
Console.WriteLine($"Pipeline: {analyses.Count} bars analysed, {swingHighs} swing highs, {swingLows} swing lows.");
Console.WriteLine($"Structure: {bosCount} BOS, {chochCount} CHoCH, {sweepCount} sweep events, {fvgZones} FVG zones.");
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
