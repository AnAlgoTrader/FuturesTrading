using System;
using System.IO;
using IndicatorBuild;

var projectRoot = FindProjectRoot();
var csvPath = Path.Combine(projectRoot, "data", "historicalData.csv");
var libraryPath = Path.Combine(projectRoot, "assets", "js", "lightweight-charts.standalone.production.js");
var outputDir = Path.Combine(projectRoot, "output");
var outputPath = Path.Combine(outputDir, "index.html");

var candles = HistoricalDataSource.LoadFromCsv(csvPath);
var html = ChartHtmlGenerator.Build(candles, libraryPath);

Directory.CreateDirectory(outputDir);
File.WriteAllText(outputPath, html);

Console.WriteLine($"Loaded {candles.Count} candles from {csvPath}");
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
