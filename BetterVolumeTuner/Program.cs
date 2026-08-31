using System.Diagnostics;
using System.Text;
using BetterVolumeTuner;

const string csvPath = "../Data/Dataset_NQ_1min_2022_2025.csv";
const string outputPath = "sweep_results.txt";

var output = new StringBuilder();
var stdout = Console.Out;
var writer = new StringWriter(output);
// Tee both to console and buffer
void Log(string s) { stdout.WriteLine(s); writer.WriteLine(s); }

Log("╔══════════════════════════════════════════════════════╗");
Log("║  Better Volume Indicator — Parameter Tuner          ║");
Log("╚══════════════════════════════════════════════════════╝");
Log("");

// Load data
Console.Write($"Loading CSV from {csvPath}... ");
var sw = Stopwatch.StartNew();
var bars = CsvBarLoader.Load(csvPath);
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
var runner = new SweepRunner(bars);
var results = runner.Run();
sw.Stop();
Console.WriteLine();
Console.WriteLine($"Sweep completed in {sw.Elapsed.TotalMinutes:F1} minutes ({results.Count} parameter sets evaluated)");
Console.WriteLine();

// Report results
ConsoleReporter.PrintTopResults(results, topN: 20);
ConsoleReporter.PrintSignalBreakdown(results.First());
ConsoleReporter.PrintSignalDistribution(results);
ConsoleReporter.PrintParameterSensitivity(results);

Console.WriteLine("Done.");
