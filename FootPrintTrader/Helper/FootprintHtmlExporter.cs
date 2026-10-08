using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FootPrintTrader.Helper;

public static class FootprintHtmlExporter
{
    public static string Build(FootprintRenderSnapshot snapshot, DateTime timestamp)
    {
        var wwwRoot = FindWwwRoot();
        var renderer = File.ReadAllText(Path.Combine(wwwRoot, "assets", "js", "footprint-render.js"));
        var css = File.ReadAllText(Path.Combine(wwwRoot, "assets", "main.css"));
        var json = JsonSerializer.Serialize(snapshot, FootprintRenderBuilder.JsonOptions)
            .Replace("</", "<\\/");

        var html = new StringBuilder();
        html.Append("<!DOCTYPE html>\n")
            .Append("<html lang=\"en\">\n<head>\n")
            .Append("<meta charset=\"utf-8\">\n")
            .Append("<title>Footprint ").Append(timestamp.ToString("yyyy-MM-dd HH:mm")).Append("</title>\n")
            .Append("<style>\n").Append(css).Append("\n</style>\n")
            .Append("</head>\n<body>\n")
            .Append("<div id=\"wrap\"><canvas id=\"cv\"></canvas><div id=\"msg\"></div></div>\n")
            .Append("<script>\n").Append(renderer).Append("\n</script>\n")
            .Append("<script>\n")
            .Append("const data = ").Append(json).Append(";\n")
            .Append("if (data.bars && data.bars.length) { footprint.load(data); footprint.showAll(); }\n")
            .Append("</script>\n")
            .Append("</body>\n</html>\n");
        return html.ToString();
    }

    private static string FindWwwRoot()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"),
            Path.Combine(AppContext.BaseDirectory, "wwwroot")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(Path.Combine(candidate, "assets", "js", "footprint-render.js")))
                return candidate;
        }

        throw new DirectoryNotFoundException($"wwwroot not found: {string.Join(", ", candidates)}");
    }
}
