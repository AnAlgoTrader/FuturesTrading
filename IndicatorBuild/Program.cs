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
using IndicatorBuild.Streaming;

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
var outWin = IndicatorConfig.OutcomeForwardBars;
var outMin = IndicatorConfig.OutcomeMinWinAtr;
var gate = IndicatorConfig.ConfluenceGate;
var holdoutFraction = IndicatorConfig.OutcomeHoldoutFraction;
var stream = false;
var streamLimit = 0;

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
        case "--outwin" when i + 1 < args.Length: outWin = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--outmin" when i + 1 < args.Length: outMin = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--gate" when i + 1 < args.Length: gate = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--holdout" when i + 1 < args.Length: holdoutFraction = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--stream": stream = true; break;
        case "--streamlimit" when i + 1 < args.Length: streamLimit = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
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
    BuildEngines(pivot, sweepWick, fvgWidth, fvgBodies, fvgFill, conBars, conAtr, holdBars,
        pbLeg, pbMin, pbMax, wycSwings, wycBars, wycMaxAtr, wycPrior, momWi, momVol, momDiv, momExp));
var analyses = pipeline.Run(candles);
var swingHighs = pipeline.Swings.Count(s => s.IsHigh);
var swingLows = pipeline.Swings.Count(s => !s.IsHigh);

var paramsLine = $"Params: pivot={pivot} sweepWick={sweepWick:0.00} fvgWidth={fvgWidth:0.00} "
    + $"fvgBodies={fvgBodies} fvgFill={fvgFill:0.00} conBars={conBars} conAtr={conAtr:0.00} hold={holdBars} "
    + $"pbLeg={pbLeg:0.00} pb[{pbMin:0.000}-{pbMax:0.000}] wycSwings={wycSwings} wycBars={wycBars} "
    + $"wycMaxAtr={wycMaxAtr:0.00} wycPrior={wycPrior:0.00} momWi={momWi:0.00} momVol={momVol:0.00} "
    + $"momDiv={momDiv:0.000} momExp={momExp:0.00} outWin={outWin} outMin={outMin:0.00} gate={gate}"
    + $" holdout={holdoutFraction:0.00}";

var metrics = BacktestStats.Collect(candles, pipeline.Events, pipeline.Zones, pivot, sweepWick, fvgWidth);
Console.WriteLine($"Loaded {candles.Count} candles from {csvPath}");
Console.WriteLine($"Pipeline: {analyses.Count} bars analysed, {swingHighs} swing highs, {swingLows} swing lows.");
Console.WriteLine(paramsLine);
Console.WriteLine("M2-M7 quality metrics:");
Console.WriteLine(metrics);
Console.WriteLine($"Structure totals: {pipeline.Events.Count(e => e.Type == Concept.BOS)} BOS, "
    + $"{pipeline.Events.Count(e => e.Type == Concept.ChoCh)} CHoCH, "
    + $"{pipeline.Events.Count(e => e.Type == Concept.LiquiditySweep)} sweep events, "
    + $"{pipeline.Zones.Count(z => z.Kind == ZoneKind.FairValueGap)} FVG zones.");

ConfluenceScorer.Score(candles, analyses, pipeline.Zones, pipeline.Swings, gate);

if (stream)
    RunStreamingReplay(pipeline, candles.Count, gate, streamLimit);
var model = new ChartModelBuilder().Build(candles, vwap, analyses, pipeline.Zones);
var html = ChartHtmlGenerator.Build(model, libraryPath);

var outcomesAll = OutcomeStats.Collect(candles, pipeline.Events, outWin, outMin);
var outcomesGated = OutcomeStats.Collect(candles, pipeline.Events, outWin, outMin,
    include: e => e.Qualified || !ConfluenceScorer.ColoredGated.Contains(e.Type));
var comparisons = OutcomeCompare.Build(outcomesAll, outcomesGated);

// M9 holdout: the reserved never-tuned tail. Split by bar count (feed is 1-minute, time-sorted).
var holdoutStart = candles[(int)(candles.Count * (1.0 - Math.Clamp(holdoutFraction, 0, 0.49)))].Time;
var outcomesInSample = OutcomeStats.Collect(candles, pipeline.Events, outWin, outMin,
    include: e => e.Time < holdoutStart);
var outcomesHoldout = OutcomeStats.Collect(candles, pipeline.Events, outWin, outMin,
    include: e => e.Time >= holdoutStart);
var outcomesHoldoutGated = OutcomeStats.Collect(candles, pipeline.Events, outWin, outMin,
    include: e => e.Time >= holdoutStart && (e.Qualified || !ConfluenceScorer.ColoredGated.Contains(e.Type)));
var holdoutRows = OutcomeHoldout.Build(outcomesInSample, outcomesHoldout, outcomesHoldoutGated);

var summary = new System.Text.StringBuilder();
summary.AppendLine("M7 gating report (win%: all -> gated, delta):");
foreach (var row in comparisons)
    summary.AppendLine(row.ToTable());
summary.AppendLine();
summary.AppendLine($"M9 holdout report (last {holdoutFraction:0%} of bars — never tuned; win%: in-sample | holdout | holdout-gated):");
summary.AppendLine($"  split at bar {(int)(candles.Count * (1.0 - Math.Clamp(holdoutFraction, 0, 0.49)))} of {candles.Count} (time {holdoutStart})");
foreach (var row in holdoutRows)
    summary.AppendLine(row.ToTable());
var outcomeText = summary.ToString();

Directory.CreateDirectory(outputDir);
File.WriteAllText(outputPath, html);
File.WriteAllText(Path.Combine(outputDir, "dashboard.html"),
    DashboardHtml.BuildComparison(comparisons, holdoutRows, $"SMC indicator — {candles.Count} bars", paramsLine));

Console.WriteLine($"Generated chart: {outputPath}");
Console.WriteLine($"Generated dashboard: {Path.Combine(outputDir, "dashboard.html")}");
Console.WriteLine(outcomeText);

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

static IBarEngine[] BuildEngines(int pivot, double sweepWick, double fvgWidth, bool fvgBodies, double fvgFill,
    int conBars, double conAtr, int holdBars, double pbLeg, double pbMin, double pbMax,
    int wycSwings, int wycBars, double wycMaxAtr, double wycPrior, double momWi, double momVol, double momDiv, double momExp)
    => new IBarEngine[]
    {
        new MarketStructureEngine(),
        new LiquiditySweepDetector(sweepWick),
        new FvgDetector(fvgWidth, fvgBodies, fvgFill),
        new ConsolidationEngine(conBars, conAtr, holdBars),
        new PullbackDetector(pbLeg, pbMin, pbMax),
        new WyckoffEngine(wycSwings, wycBars, IndicatorConfig.WyckoffRangeMinHeightAtr, wycMaxAtr, wycPrior),
        new MomentumEngine(momWi, momVol, momDiv, momExp)
    };

/// <summary>
/// M8 streaming mode: replay the historical CSV through the exact same pipeline as the
/// backtest, but one closed bar at a time through a <see cref="StreamingSession"/>, and
/// verify the emitted event sequence (Type/Time/Direction/Detail) matches the batch pass.
/// </summary>
void RunStreamingReplay(IndicatorPipeline batchPipeline, int totalCandles, int gate, int streamLimit)
{
    IBarSource source = new CsvBarSource(csvPath);
    if (streamLimit > 0)
        source = new LimitBarSource(source, streamLimit);

    var replayPipeline = new IndicatorPipeline(new SwingDetector(pivot, pivot),
        BuildEngines(pivot, sweepWick, fvgWidth, fvgBodies, fvgFill, conBars, conAtr, holdBars,
            pbLeg, pbMin, pbMax, wycSwings, wycBars, wycMaxAtr, wycPrior, momWi, momVol, momDiv, momExp));
    var session = new StreamingSession(replayPipeline, gate);

    var liveEvents = new List<string>();
    var liveZones = 0;
    var liveSwings = 0;
    var sampleShown = 0;
    session.EventRaised += (evt, bar) =>
    {
        liveEvents.Add(Signature(evt));
        if (sampleShown++ < 5)
            Console.WriteLine($"    [bar {bar,6}] {evt.Type} {evt.Direction,-8} {evt.Detail,-10} score={evt.Score} q={evt.Qualified}");
    };
    session.ZoneCreated += _ => liveZones++;
    session.SwingConfirmed += _ => liveSwings++;
    session.BarClosed += (_, bar) =>
    {
        if (bar % Math.Max(1, (streamLimit > 0 ? streamLimit : totalCandles) / 10) == 0)
            Console.WriteLine($"    [bar {bar,6}] processed {(double)bar / (streamLimit > 0 ? streamLimit : totalCandles):P0}");
    };

    var bars = session.Run(source);

    Console.WriteLine("M8 streaming replay:");
    Console.WriteLine($"  Bars consumed: {bars} of {totalCandles}");
    Console.WriteLine($"  Live callbacks: swings={liveSwings} zones={liveZones} events={liveEvents.Count}");
    Console.WriteLine($"  Pipeline totals: swings={replayPipeline.Swings.Count} zones={replayPipeline.Zones.Count} events={replayPipeline.Events.Count}");

    var batchSignatures = batchPipeline.Events.Select(Signature).ToList();
    var match = liveEvents.SequenceEqual(batchSignatures);
    Console.WriteLine(match
        ? $"  PARITY OK — event sequence (Type/Time/Direction/Detail) identical across {liveEvents.Count} events"
        : $"  PARITY MISMATCH — batch={batchSignatures.Count} stream={liveEvents.Count} (first diff: {FirstDiffIndex(liveEvents, batchSignatures)})");

    var gatedBatch = batchPipeline.Events.Where(e => ConfluenceScorer.ColoredGated.Contains(e.Type)).ToList();
    var gatedStream = replayPipeline.Events.Where(e => ConfluenceScorer.ColoredGated.Contains(e.Type)).ToList();
    var scoreMatches = gatedBatch.Zip(gatedStream, (b, s) => b.Score == s.Score).Count(x => x);
    var streamHigher = gatedBatch.Zip(gatedStream, (b, s) => s.Score > b.Score).Count(x => x);
    var streamLower = gatedBatch.Zip(gatedStream, (b, s) => s.Score < b.Score).Count(x => x);
    Console.WriteLine($"  Confluence score parity: {scoreMatches}/{gatedStream.Count} identical; "
        + $"{streamLower} stream-lower (zone not yet formed — expected), {streamHigher} stream-higher (would indicate a bug)");
    if (streamHigher > 0)
    {
        var i = gatedBatch.Zip(gatedStream, (b, s) => (B: b, S: s)).First(p => p.S.Score > p.B.Score);
        Console.WriteLine($"    first inversion: batch={i.B.Score} stream={i.S.Score} {i.B.Type} {i.B.Direction} {i.B.Detail}");
    }
}

static string Signature(ConceptEvent evt) =>
    $"{evt.Type}|{evt.Time}|{evt.Direction}|{evt.Detail}";

static int FirstDiffIndex(List<string> stream, List<string> batch)
{
    var n = Math.Min(stream.Count, batch.Count);
    for (var i = 0; i < n; i++)
        if (stream[i] != batch[i]) return i;
    return batch.Count;
}
