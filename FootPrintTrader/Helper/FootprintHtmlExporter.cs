using System;
using System.IO;
using System.Text.Json;

namespace FootPrintTrader.Helper;

public static class FootprintHtmlExporter
{
    public static string Build(FootprintRenderSnapshot snapshot, DateTime timestamp)
    {
        var wwwRoot = FindWwwRoot();
        var template = File.ReadAllText(Path.Combine(wwwRoot, "index.html"));
        var renderer = File.ReadAllText(Path.Combine(wwwRoot, "assets", "js", "footprint-render.js"));
        var mainJs = File.ReadAllText(Path.Combine(wwwRoot, "assets", "js", "main.js"));
        var css = File.ReadAllText(Path.Combine(wwwRoot, "assets", "main.css"));
        var json = JsonSerializer.Serialize(snapshot, FootprintRenderBuilder.JsonOptions)
            .Replace("</", "<\\/");

        var initScript = $@"
<script>
const data = {json};
if (data.bars && data.bars.length) {{
    footprint.load(data);
    footprint.showAll();
    if (data.context) updateSidePanel(data.context, data.rowSize || 0.25);
    if (data.interpretation) updateInterpretationPanel(data.interpretation);
}}
</script>";

        var html = template
            .Replace("<link rel=\"stylesheet\" href=\"assets/main.css\" />", $"<style>\n{css}\n</style>")
            .Replace("<script src=\"app://dynamic.js\"></script>", "")
            .Replace("<script src=\"assets/js/footprint-render.js\"></script>", $"<script>\n{renderer}\n</script>")
            .Replace("<script src=\"assets/js/main.js\"></script>", $"<script>\n{mainJs}\n</script>");

        var startIdx = html.IndexOf("window.external.receiveMessage", StringComparison.Ordinal);
        if (startIdx >= 0)
        {
            var scriptOpen = html.LastIndexOf("<script", startIdx, StringComparison.Ordinal);
            var scriptClose = html.IndexOf("</script>", startIdx, StringComparison.Ordinal);
            if (scriptOpen >= 0 && scriptClose > scriptOpen)
            {
                html = html.Substring(0, scriptOpen) + initScript + html.Substring(scriptClose + "</script>".Length);
            }
        }
        else
        {
            html = html.Replace("</body>", initScript + "\n</body>");
        }

        return html;
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
