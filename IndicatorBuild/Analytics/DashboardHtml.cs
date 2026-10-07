using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IndicatorBuild.Analytics;

public static class DashboardHtml
{
    public static string Build(
        IReadOnlyList<OutcomeRow> rows,
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
        sb.AppendLine("  table{border-collapse:collapse;font-size:13px;width:100%;max-width:980px}");
        sb.AppendLine("  th,td{border:1px solid #ddd;padding:4px 8px;text-align:right;white-space:nowrap}");
        sb.AppendLine("  th{background:#f5f5f5;text-align:center}");
        sb.AppendLine("  td:first-child,td:nth-child(2),td:nth-child(3){text-align:left}");
        sb.AppendLine("  tr:nth-child(even){background:#fafafa}");
        sb.AppendLine("  .good{color:#1b8a4c}.bad{color:#c62828}.warn{color:#b26a00}");
        sb.AppendLine("  .card{border:1px solid #e0e0e0;border-radius:6px;padding:12px 16px;margin-bottom:12px;max-width:960px;font-size:13px}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine($"<h1>Outcome Dashboard — {title}</h1>");
        sb.AppendLine($"<div class=\"sub\">{paramsLine}</div>");
        sb.AppendLine("<div class=\"card\">Columns: <b>N</b> events with a full forward window; "
            + "<b>win%</b> fraction whose forward close moved ≥ 0.25×ATR in the predicted direction; "
            + "<b>avg/med</b> forward move in predicted direction (×ATR); "
            + "<b>hit1/fail1</b> fraction that showed ≥ 1×ATR favorable/adverse excursion inside the window. "
            + "Backtest-only; outcomes are never fed back into detection.</div>");
        sb.AppendLine("<table><tr><th>Signal</th><th>Detail</th><th>Direction</th><th>N</th>"
            + "<th>win%</th><th>avg(ATR)</th><th>med(ATR)</th><th>hit≥1ATR%</th><th>fail≥1ATR%</th></tr>");

        foreach (var r in rows)
        {
            var winClass = r.WinRate is null ? "" : r.WinRate >= 55 ? "good" : r.WinRate >= 45 ? "warn" : "bad";
            sb.AppendLine("<tr>"
                + $"<td>{Html(r.Label)}</td><td>{Html(r.Detail)}</td><td>{Html(r.Direction)}</td><td>{r.Count}</td>"
                + $"<td class=\"{winClass}\">{(r.WinRate is null ? "-" : r.WinRate.Value.ToString("0", CultureInfo.InvariantCulture))}</td>"
                + $"<td>{Fmt(r.AvgMoveAtr)}</td><td>{Fmt(r.MedianMoveAtr)}</td>"
                + $"<td>{r.Hit1AtrRate:0}</td><td>{r.Fail1AtrRate:0}</td></tr>");
        }

        sb.AppendLine("</table></body></html>");
        return sb.ToString();
    }

    private static string Fmt(double? v) => v is null ? "-" : v.Value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Html(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}