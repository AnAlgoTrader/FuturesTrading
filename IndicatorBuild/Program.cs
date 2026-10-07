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
var conBars = IndicatorConfig.ConsolidationMinBars;
var conAtr = IndicatorConfig.ConsolidationRangeAtrFactor;
var holdBars = IndicatorConfig.BreakoutConfirmBars;
var pbLeg = IndicatorConfig.PullbackLegMinAtr;
var pbMin = IndicatorConfig.PullbackHealthyMin;
var pbMax = IndicatorConfig.PullbackHealthyMax;
var wycSwings = IndicatorConfig.WyckoffRangeMinSwings;
var wycBars = IndicatorConfig.WyckoffRangeMinBars;
var wycMaxAtr = IndicatorConfig.WyckoffRangeMaxHeightAtr;
var wycPrior = IndicatorConfig.WyckoffPriorTrendAtr;
var momWi = IndicatorConfig.MomentumExhaustWickBody;
var momVol = IndicatorConfig.MomentumExhaustVolumeFactor;
var momDiv = IndicatorConfig.MomentumDivMinSwingFrac;
var momExp = IndicatorConfig.MomentumExpansionAtrRatio;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--pivot" when i + 1 < args.Length: pivot = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--sweep" when i + 1 < args.Length: sweepWick = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--fvg" when i + 1 < args.Length: fvgWidth = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--fvgbodies": fvgBodies = true; break;
        case "--fvgfill" when i + 1 < args.Length: fvgFill = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--conbars" when i + 1 < args.Length: conBars = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--conatr" when i + 1 < args.Length: conAtr = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--hold" when i + 1 < args.Length: holdBars = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--pbleg" when i + 1 < args.Length: pbLeg = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--pbmin" when i + 1 < args.Length: pbMin = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--pbmax" when i + 1 < args.Length: pbMax = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--wycswings" when i + 1 < args.Length: wycSwings = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--wycbars" when i + 1 < args.Length: wycBars = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--wycmaxatr" when i + 1 < args.Length: wycMaxAtr = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--wycprior" when i + 1 < args.Length: wycPrior = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--momwi" when i + 1 < args.Length: momWi = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--momvol" when i + 1 < args.Length: momVol = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--momdiv" when i + 1 < args.Length: momDiv = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--momexp" when i + 1 < args.Length: momExp = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
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
    new FvgDetector(fvgWidth, fvgBodies, fvgFill),
    new ConsolidationEngine(conBars, conAtr, holdBars),
    new PullbackDetector(pbLeg, pbMin, pbMax),
    new WyckoffEngine(wycSwings, wycBars, IndicatorConfig.WyckoffRangeMinHeightAtr, wycMaxAtr, wycPrior),
    new MomentumEngine(momWi, momVol, momDiv, momExp));
var analyses = pipeline.Run(candles);
var swingHighs = pipeline.Swings.Count(s => s.IsHigh);
var swingLows = pipeline.Swings.Count(s => !s.IsHigh);

var metrics = BacktestStats.Collect(candles, pipeline.Events, pipeline.Zones, pivot, sweepWick, fvgWidth);
Console.WriteLine($"Loaded {candles.Count} candles from {csvPath}");
Console.WriteLine($"Pipeline: {analyses.Count} bars analysed, {swingHighs} swing highs, {swingLows} swing lows.");
Console.WriteLine($"Params: pivot={pivot} sweepWick={sweepWick:0.00} fvgWidth={fvgWidth:0.00} fvgBodies={fvgBodies} fvgFill={fvgFill:0.00} "
    + $"conBars={conBars} conAtr={conAtr:0.00} hold={holdBars} pbLeg={pbLeg:0.00} pb[{pbMin:0.000}-{pbMax:0.000}] "
    + $"wycSwings={wycSwings} wycBars={wycBars} wycMaxAtr={wycMaxAtr:0.00} wycPrior={wycPrior:0.00} "
    + $"momWi={momWi:0.00} momVol={momVol:0.00} momDiv={momDiv:0.000} momExp={momExp:0.00}");
Console.WriteLine("M2-M5 quality metrics:");
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
