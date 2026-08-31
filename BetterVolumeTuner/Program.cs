using System.Diagnostics;
using System.Text;

const string csvPath = "../Data/Dataset_NQ_1min_2022_2025.csv";
const string outputPath = "sweep_results.txt";

// Tee Console.Out to both stdout and a file
var fileWriter = new StreamWriter(outputPath) { AutoFlush = true };
var originalOut = Console.Out;
var teeWriter = new TeeWriter(originalOut, fileWriter);
Console.SetOut(teeWriter);

Console.WriteLine("╔══════════════════════════════════════════════════════╗");
Console.WriteLine("║  Better Volume Indicator — Parameter Tuner           ║");
Console.WriteLine("╚══════════════════════════════════════════════════════╝");
Console.WriteLine();

// Load data
Console.Write($"Loading CSV from {csvPath}... ");
var sw = Stopwatch.StartNew();
var bars = BetterVolumeTuner.CsvBarLoader.Load(csvPath);
sw.Stop();
Console.WriteLine($"{bars.Count:N0} bars loaded in {sw.Elapsed.TotalSeconds:F1}s");

if (bars.Count == 0)
{
    Console.WriteLine("ERROR: No bars loaded. Check the CSV path.");
    return;
}

// Show data summary
Console.WriteLine();
Console.WriteLine("── Data Summary ──");
Console.WriteLine($"  Date range: {DateTimeOffset.FromUnixTimeSeconds(bars.First().StartTime).LocalDateTime:yyyy-MM-dd} to {DateTimeOffset.FromUnixTimeSeconds(bars.Last().StartTime).LocalDateTime:yyyy-MM-dd}");
Console.WriteLine($"  Total bars: {bars.Count:N0}");
Console.WriteLine($"  Avg volume: {bars.Average(b => b.Volume):F0}");
Console.WriteLine($"  Avg spread: {bars.Average(b => b.Spread):F2}");
Console.WriteLine();

// Run sweep
Console.WriteLine("Starting parameter sweep (2880 combinations)...");
Console.WriteLine();
sw.Restart();
var runner = new BetterVolumeTuner.SweepRunner(bars);
var results = runner.Run();
sw.Stop();
Console.WriteLine();
Console.WriteLine($"Sweep completed in {sw.Elapsed.TotalMinutes:F1} minutes ({results.Count} parameter sets evaluated)");
Console.WriteLine();

// Report results
BetterVolumeTuner.ConsoleReporter.PrintTopResults(results, topN: 20);
BetterVolumeTuner.ConsoleReporter.PrintSignalBreakdown(results.First());
BetterVolumeTuner.ConsoleReporter.PrintSignalDistribution(results);
BetterVolumeTuner.ConsoleReporter.PrintParameterSensitivity(results);

Console.WriteLine($"Done. Results saved to {outputPath}");

// Flush and restore
fileWriter.Flush();
Console.SetOut(originalOut);
fileWriter.Close();

/// <summary>
/// TextWriter that tees output to two writers simultaneously.
/// </summary>
class TeeWriter : TextWriter
{
    private readonly TextWriter _a;
    private readonly TextWriter _b;

    public TeeWriter(TextWriter a, TextWriter b) { _a = a; _b = b; }

    public override void Write(char value) { _a.Write(value); _b.Write(value); }
    public override void Write(string? value) { _a.Write(value); _b.Write(value); }
    public override void Write(char[] buffer, int index, int count) { _a.Write(buffer, index, count); _b.Write(buffer, index, count); }
    public override void WriteLine(string? value) { _a.WriteLine(value); _b.WriteLine(value); }
    public override void WriteLine() { _a.WriteLine(); _b.WriteLine(); }
    public override Encoding Encoding => Encoding.UTF8;
}
