using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IndicatorBuild.Analytics;

public static class DashboardHtml
{
    public static string BuildComparison(
        IReadOnlyList<OutcomeComparison> rows,
        IReadOnlyList<OutcomeHoldoutRow>? holdout,
        string title,
        string paramsLine)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<title>SMC Indicator — Outcome Dashboard</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("  body{font-family:system-ui,sans-serif;margin:24px;color:#222;background:#fff}");
        sb.AppendLine("  h1{font-size:20px;margin:0 0 4px}");
        sb.AppendLine("  .sub{color:#666;font-size:12px;margin-bottom:16px}");
        sb.AppendLine("  table{border-collapse:collapse;font-size:13px;width:100%;max-width:1100px}");
        sb.AppendLine("  th,td{border:1px solid #ddd;padding:4px 8px;text-align:right;white-space:nowrap}");
        sb.AppendLine("  th{background:#f5f5f5;text-align:center}");
        sb.AppendLine("  td:first-child,td:nth-child(2),td:nth-child(3){text-align:left}");
        sb.AppendLine("  tr:nth-child(even){background:#fafafa}");
        sb.AppendLine("  .good{color:#1b8a4c}.bad{color:#c62828}.warn{color:#b26a00}");
        sb.AppendLine("  .card{border:1px solid #e0e0e0;border-radius:6px;padding:12px 16px;margin-bottom:12px;max-width:1080px;font-size:13px}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine($"<h1>Outcome Dashboard — {title}</h1>");
        sb.AppendLine($"<div class=\"sub\">{Html(paramsLine)}</div>");
        sb.AppendLine("<div class=\"card\"><b>Confluence gate M7.</b> \"all\" = every event with a full 10-bar forward window; "
            + "\"gated\" = the subset that passed the confluence gate score (summing the bar's trend/RSI/body/volume factors). "
            + "win% = forward close moved &gt;= 0.25x ATR in the predicted direction. "
            + "The &#916; column is the precision change from gating (positive = gating improved that signal). "
            + "Outcomes are backtest-only and never fed back into detection.</div>");
        sb.AppendLine("<table><tr><th>Signal</th><th>Detail</th><th>Direction</th>"
            + "<th>All N</th><th>All win%</th><th>Gated N</th><th>Gated win%</th><th>&#916;</th></tr>");

        foreach (var r in rows)
        {
            var gatedWin = r.GatedWin;
            var delta = r.AllWin is null || gatedWin is null ? (double?)null : gatedWin - r.AllWin;
            var winClass = gatedWin is null ? "" : gatedWin >= 55 ? "good" : gatedWin >= 45 ? "warn" : "bad";
            var deltaClass = delta is null ? "" : delta > 0 ? "good" : delta < 0 ? "bad" : "";
            sb.AppendLine("<tr>"
                + $"<td>{Html(r.Label)}</td><td>{Html(r.Detail)}</td><td>{Html(r.Direction)}</td>"
                + $"<td>{r.AllCount}</td><td>{FmtInt(r.AllWin)}</td>"
                + $"<td>{r.GatedCount}</td><td class=\"{winClass}\">{FmtInt(gatedWin)}</td>"
                + $"<td class=\"{deltaClass}\">{FmtDelta(delta)}</td></tr>");
        }

        sb.AppendLine("</table>");

        if (holdout is { Count: > 0 })
        {
            sb.AppendLine("<h2 style=\"font-size:15px;margin:20px 0 6px\">M9 holdout validation — never-tuned tail</h2>");
            sb.AppendLine("<div class=\"sub\">Reserved last bars (default 20%). Tuning never saw these. "
                + "If the holdout win% collapses vs in-sample, the edge was overfit.</div>");
            sb.AppendLine("<table><tr><th>Signal</th><th>Detail</th><th>Direction</th>"
                + "<th>In-sample N</th><th>In-sample win%</th><th>Holdout N</th><th>Holdout win%</th>"
                + "<th>Holdout gated N</th><th>Holdout gated win%</th></tr>");
            foreach (var r in holdout)
            {
                var cls = r.HoldoutWin is null ? "" : r.HoldoutWin >= 55 ? "good" : r.HoldoutWin >= 45 ? "warn" : "bad";
                sb.AppendLine("<tr>"
                    + $"<td>{Html(r.Label)}</td><td>{Html(r.Detail)}</td><td>{Html(r.Direction)}</td>"
                    + $"<td>{r.InCount}</td><td>{FmtInt(r.InWin)}</td>"
                    + $"<td>{r.HoldoutCount}</td><td class=\"{cls}\">{FmtInt(r.HoldoutWin)}</td>"
                    + $"<td>{r.GatedCount}</td><td>{FmtInt(r.GatedWin)}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string FmtInt(double? v) => v is null ? "-" : ((int)v.Value).ToString(CultureInfo.InvariantCulture);

    private static string FmtDelta(double? v) =>
        v is null ? "-" : v.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture);

    private static string Html(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}