using System;
using System.IO;
using System.Text.Json;

namespace FootPrintTrader.Helper;

public static class FootprintHtmlExporter
{
    public static string Build(FootprintRenderSnapshot snapshot, DateTime timestamp)
    {
        var wwwRoot = FindWwwRoot();
        var template = File.ReadAllText(Path.Combine(wwwRoot, "futures.html"));
        var renderer = File.ReadAllText(Path.Combine(wwwRoot, "assets", "js", "footprint-render.js"));
        var futuresJs = File.ReadAllText(Path.Combine(wwwRoot, "assets", "js", "futures.js"));
        var css = File.ReadAllText(Path.Combine(wwwRoot, "assets", "futures.css"));
        var json = JsonSerializer.Serialize(snapshot, FootprintRenderBuilder.JsonOptions)
            .Replace("</", "<\\/");

        var initScript = $@"
<script>
const data = {json};
if (data.bars && data.bars.length) {{
    footprint.load(data);
    footprint.showLatest(12);
    if (data.context) updateSidePanel(data.context, data.rowSize || 0.25);

    const hint = document.createElement('div');
    hint.textContent = 'Shift/Ctrl + wheel or + / - = time zoom  ·  drag = pan  ·  0 = show all';
    hint.style.cssText = 'position:fixed;left:50%;bottom:16px;transform:translateX(-50%);background:rgba(23,28,38,.94);border:1px solid #262d3b;color:#8a93a6;padding:6px 12px;border-radius:6px;font:12px system-ui,sans-serif;z-index:40;pointer-events:none;box-shadow:0 4px 12px rgba(0,0,0,.4)';
    document.body.appendChild(hint);
    setTimeout(() => hint.remove(), 12000);
}}
</script>";

        var html = template
            .Replace("<link rel=\"stylesheet\" href=\"assets/futures.css\" />", $"<style>\n{css}\n</style>")
            .Replace("<script src=\"app://dynamic.js\"></script>", "")
            .Replace("<script src=\"assets/js/footprint-render.js\"></script>", $"<script>\n{renderer}\n</script>")
            .Replace("<script src=\"assets/js/futures.js\"></script>", $"<script>\n{futuresJs}\n</script>");

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
