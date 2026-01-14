using System.Text;
using Blackpaw.Analysis;

namespace Blackpaw.Reporting;

/// <summary>
/// Generates HTML comparison reports between two performance runs.
/// Uses the same charcoal grey memorial theme as the single-run report.
/// </summary>
public static class ComparisonReportGenerator
{
    public static string GenerateHtml(RunComparison comparison, ReportData baselineData, ReportData targetData)
    {
        var sb = new StringBuilder();

        AppendHead(sb, comparison);
        sb.AppendLine("<body>");

        AppendHeader(sb, comparison);
        AppendHealthIndicators(sb, comparison);
        AppendVerdictSection(sb, comparison);
        AppendSystemComparison(sb, comparison);
        AppendProcessComparison(sb, comparison);
        AppendDotNetComparison(sb, comparison);
        AppendHttpComparison(sb, comparison);
        AppendSqlComparison(sb, comparison);
        AppendOverlayCharts(sb, comparison, baselineData, targetData);
        AppendFooter(sb);

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static void AppendHead(StringBuilder sb, RunComparison comparison)
    {
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"  <title>Comparison: #{comparison.Baseline.RunId} vs #{comparison.Target.RunId}</title>");
        sb.AppendLine("  <script src=\"https://cdn.jsdelivr.net/npm/chart.js\"></script>");
        sb.AppendLine("  <script src=\"https://cdn.jsdelivr.net/npm/chartjs-adapter-date-fns\"></script>");
        sb.AppendLine("  <style>");
        sb.AppendLine(@"
    :root {
      --bg: #2d2d2d;
      --bg-card: #3a3a3a;
      --bg-hover: #454545;
      --text: #e8e8e8;
      --text-dim: #a0a0a0;
      --accent: #4a4a4a;
      --highlight: #d4a574;
      --highlight-soft: #c49a6c;
      --success: #7eb77f;
      --warning: #d4a574;
      --error: #c77272;
      --baseline-color: #8da0cb;
      --target-color: #66c2a5;
    }
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body {
      font-family: 'Segoe UI', system-ui, sans-serif;
      background: var(--bg);
      color: var(--text);
      line-height: 1.6;
      padding: 2rem;
    }
    .container { max-width: 1400px; margin: 0 auto; }
    h1, h2, h3 { color: var(--text); font-weight: 500; }
    h1 { font-size: 1.5rem; margin-bottom: 0.5rem; }
    h2 { font-size: 1.2rem; margin-bottom: 1rem; color: var(--highlight); }

    .header {
      background: var(--bg-card);
      border-radius: 12px;
      padding: 1.5rem 2rem;
      margin-bottom: 1.5rem;
      border-left: 4px solid var(--highlight);
    }
    .header-title {
      display: flex;
      align-items: center;
      gap: 1rem;
      margin-bottom: 1rem;
    }
    .header-title h1 { margin: 0; }
    .run-info {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 1rem;
    }
    .run-card {
      background: var(--bg);
      border-radius: 8px;
      padding: 1rem;
    }
    .run-card.baseline { border-left: 3px solid var(--baseline-color); }
    .run-card.target { border-left: 3px solid var(--target-color); }
    .run-label { font-size: 0.85rem; color: var(--text-dim); margin-bottom: 0.25rem; }
    .run-name { font-size: 1.1rem; font-weight: 500; }
    .run-meta { font-size: 0.85rem; color: var(--text-dim); margin-top: 0.25rem; }

    .verdict {
      background: var(--bg-card);
      border-radius: 12px;
      padding: 1.5rem 2rem;
      margin-bottom: 1.5rem;
      text-align: center;
    }
    .verdict-status {
      font-size: 1.5rem;
      font-weight: 600;
      margin-bottom: 0.5rem;
    }
    .verdict-status.improved { color: var(--success); }
    .verdict-status.regressed { color: var(--error); }
    .verdict-status.mixed { color: var(--warning); }
    .verdict-summary { color: var(--text-dim); }
    .verdict-details {
      display: flex;
      justify-content: center;
      gap: 2rem;
      margin-top: 1rem;
    }
    .verdict-stat {
      display: flex;
      flex-direction: column;
      align-items: center;
    }
    .verdict-stat-value { font-size: 1.5rem; font-weight: 600; }
    .verdict-stat-label { font-size: 0.85rem; color: var(--text-dim); }

    .section {
      background: var(--bg-card);
      border-radius: 12px;
      padding: 1.5rem;
      margin-bottom: 1.5rem;
    }

    table {
      width: 100%;
      border-collapse: collapse;
      font-size: 0.9rem;
    }
    th, td {
      padding: 0.75rem 1rem;
      text-align: left;
      border-bottom: 1px solid var(--accent);
    }
    th {
      color: var(--text-dim);
      font-weight: 500;
      font-size: 0.85rem;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }
    td.number { text-align: right; font-family: 'Cascadia Code', monospace; }
    tr:hover { background: var(--bg-hover); }

    .change { display: flex; align-items: center; gap: 0.5rem; }
    .change-arrow { font-weight: 600; }
    .change-value { font-family: 'Cascadia Code', monospace; }
    .change-icon { font-size: 1rem; }
    .improved { color: var(--success); }
    .regressed { color: var(--error); }
    .neutral { color: var(--text-dim); }

    .status-new { color: var(--highlight); font-style: italic; }
    .status-removed { color: var(--text-dim); text-decoration: line-through; }

    .key-changes {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 1rem;
      margin-top: 1rem;
    }
    .key-changes-list {
      background: var(--bg);
      border-radius: 8px;
      padding: 1rem;
    }
    .key-changes-list h3 {
      font-size: 0.95rem;
      margin-bottom: 0.75rem;
    }
    .key-changes-list ul { list-style: none; }
    .key-changes-list li {
      padding: 0.25rem 0;
      font-size: 0.9rem;
    }
    .key-changes-list.improvements h3 { color: var(--success); }
    .key-changes-list.regressions h3 { color: var(--error); }

    .chart-container {
      background: var(--bg);
      border-radius: 8px;
      padding: 1rem;
      margin-top: 1rem;
      height: 300px;
    }
    .chart-legend {
      display: flex;
      justify-content: center;
      gap: 2rem;
      margin-top: 0.5rem;
      font-size: 0.85rem;
    }
    .legend-item {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .legend-color {
      width: 12px;
      height: 12px;
      border-radius: 2px;
    }
    .legend-baseline { background: var(--baseline-color); }
    .legend-target { background: var(--target-color); }

    .footer {
      text-align: center;
      padding: 2rem;
      color: var(--text-dim);
      font-size: 0.85rem;
    }
    .memorial {
      margin-top: 0.5rem;
      font-style: italic;
      color: var(--highlight-soft);
    }

    /* Health Indicators */
    .health-indicators {
      background: var(--bg-card);
      border-radius: 12px;
      padding: 1.5rem;
      margin-bottom: 1.5rem;
    }
    .health-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
      gap: 1rem;
      margin-top: 1rem;
    }
    .health-card {
      background: var(--bg);
      padding: 1rem;
      border-radius: 8px;
      text-align: center;
      border-left: 4px solid var(--accent);
    }
    .health-card.health-green { border-left-color: var(--success); background: rgba(126, 183, 127, 0.1); }
    .health-card.health-yellow { border-left-color: var(--warning); background: rgba(212, 165, 116, 0.1); }
    .health-card.health-red { border-left-color: var(--error); background: rgba(199, 114, 114, 0.15); }
    .health-value { font-size: 1.75rem; font-weight: 600; }
    .health-label { font-size: 0.85rem; color: var(--text-dim); margin-top: 0.25rem; }
    .health-delta { font-size: 0.8rem; margin-top: 0.5rem; font-family: 'Cascadia Code', monospace; }

    /* Exception Alert */
    .exception-alert {
      display: flex;
      align-items: center;
      gap: 1rem;
      padding: 1rem;
      border-radius: 8px;
      margin-bottom: 1rem;
    }
    .exception-alert.health-green { background: rgba(126, 183, 127, 0.1); border-left: 4px solid var(--success); }
    .exception-alert.health-yellow { background: rgba(212, 165, 116, 0.1); border-left: 4px solid var(--warning); }
    .exception-alert.health-red { background: rgba(199, 114, 114, 0.15); border-left: 4px solid var(--error); }
    .exception-count { font-size: 1.5rem; font-weight: 700; }
    .exception-label { font-size: 0.9rem; color: var(--text-dim); }
    .exception-change { font-size: 0.85rem; font-family: 'Cascadia Code', monospace; }

    /* Process subsections */
    .process-section { margin-bottom: 1.5rem; }
    .process-section h3 { font-size: 1rem; margin-bottom: 0.75rem; color: var(--highlight); }

    @media (max-width: 768px) {
      body { padding: 1rem; }
      .run-info { grid-template-columns: 1fr; }
      .key-changes { grid-template-columns: 1fr; }
      .verdict-details { flex-direction: column; gap: 1rem; }
      .health-grid { grid-template-columns: 1fr; }
    }
        ");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
    }

    private static void AppendHeader(StringBuilder sb, RunComparison comparison)
    {
        sb.AppendLine("<div class=\"container\">");
        sb.AppendLine("  <div class=\"header\">");
        sb.AppendLine("    <div class=\"header-title\">");
        sb.AppendLine("      <h1>Blackpaw Run Comparison</h1>");
        sb.AppendLine("    </div>");
        sb.AppendLine("    <div class=\"run-info\">");

        // Baseline
        sb.AppendLine("      <div class=\"run-card baseline\">");
        sb.AppendLine("        <div class=\"run-label\">BASELINE</div>");
        sb.AppendLine($"        <div class=\"run-name\">#{comparison.Baseline.RunId} {HtmlEncode(comparison.Baseline.ScenarioName)}</div>");
        sb.AppendLine($"        <div class=\"run-meta\">{comparison.Baseline.StartedAtUtc:yyyy-MM-dd HH:mm} UTC | {FormatDuration(comparison.Baseline.DurationSeconds)} | {comparison.Baseline.SampleCount} samples</div>");
        sb.AppendLine("      </div>");

        // Target
        sb.AppendLine("      <div class=\"run-card target\">");
        sb.AppendLine("        <div class=\"run-label\">TARGET</div>");
        sb.AppendLine($"        <div class=\"run-name\">#{comparison.Target.RunId} {HtmlEncode(comparison.Target.ScenarioName)}</div>");
        sb.AppendLine($"        <div class=\"run-meta\">{comparison.Target.StartedAtUtc:yyyy-MM-dd HH:mm} UTC | {FormatDuration(comparison.Target.DurationSeconds)} | {comparison.Target.SampleCount} samples</div>");
        sb.AppendLine("      </div>");

        sb.AppendLine("    </div>");
        sb.AppendLine("  </div>");
    }

    private static void AppendHealthIndicators(StringBuilder sb, RunComparison comparison)
    {
        var result = comparison.Result;

        // Calculate health metrics
        var totalExceptions = result.DotNetApps
            .Where(a => a.Status == ComparisonStatus.Present && a.Exceptions != null)
            .Sum(a => a.Exceptions!.TargetValue);
        var baselineExceptions = result.DotNetApps
            .Where(a => a.Status == ComparisonStatus.Present && a.Exceptions != null)
            .Sum(a => a.Exceptions!.BaselineValue);

        var httpErrorRate = result.HttpEndpoints
            .Where(e => e.Status == ComparisonStatus.Present && e.ErrorRate != null)
            .Select(e => e.ErrorRate!.TargetValue)
            .DefaultIfEmpty(0)
            .Average();
        var baselineHttpErrorRate = result.HttpEndpoints
            .Where(e => e.Status == ComparisonStatus.Present && e.ErrorRate != null)
            .Select(e => e.ErrorRate!.BaselineValue)
            .DefaultIfEmpty(0)
            .Average();

        var sqlBlocked = result.Sql?.BlockedRequestsAvg.TargetValue ?? 0;
        var baselineSqlBlocked = result.Sql?.BlockedRequestsAvg.BaselineValue ?? 0;

        sb.AppendLine("  <div class=\"health-indicators\">");
        sb.AppendLine("    <h2>Health Check</h2>");
        sb.AppendLine("    <div class=\"health-grid\">");

        // Exceptions
        var exceptionsClass = GetHealthClass(totalExceptions, 0, 10, 100);
        var exceptionsDelta = FormatDelta(baselineExceptions, totalExceptions);
        sb.AppendLine($"      <div class=\"health-card {exceptionsClass}\">");
        sb.AppendLine($"        <div class=\"health-value\">{totalExceptions:N0}</div>");
        sb.AppendLine("        <div class=\"health-label\">Exceptions</div>");
        sb.AppendLine($"        <div class=\"health-delta\">{exceptionsDelta}</div>");
        sb.AppendLine("      </div>");

        // HTTP Error Rate
        var httpClass = GetHealthClass(httpErrorRate, 1, 5, 10);
        var httpDelta = FormatDelta(baselineHttpErrorRate, httpErrorRate, "%");
        sb.AppendLine($"      <div class=\"health-card {httpClass}\">");
        sb.AppendLine($"        <div class=\"health-value\">{httpErrorRate:N1}%</div>");
        sb.AppendLine("        <div class=\"health-label\">HTTP Error Rate</div>");
        sb.AppendLine($"        <div class=\"health-delta\">{httpDelta}</div>");
        sb.AppendLine("      </div>");

        // SQL Blocked Requests
        if (result.Sql != null)
        {
            var sqlClass = GetHealthClass(sqlBlocked, 0, 1, 5);
            var sqlDelta = FormatDelta(baselineSqlBlocked, sqlBlocked);
            sb.AppendLine($"      <div class=\"health-card {sqlClass}\">");
            sb.AppendLine($"        <div class=\"health-value\">{sqlBlocked:N1}</div>");
            sb.AppendLine("        <div class=\"health-label\">SQL Blocked</div>");
            sb.AppendLine($"        <div class=\"health-delta\">{sqlDelta}</div>");
            sb.AppendLine("      </div>");
        }

        // Total HTTP Requests (for context)
        var totalRequests = result.HttpEndpoints.Sum(e => e.TargetRequests);
        var baselineRequests = result.HttpEndpoints.Sum(e => e.BaselineRequests);
        if (totalRequests > 0 || baselineRequests > 0)
        {
            var requestsDelta = FormatDelta(baselineRequests, totalRequests);
            sb.AppendLine($"      <div class=\"health-card\">");
            sb.AppendLine($"        <div class=\"health-value\">{totalRequests:N0}</div>");
            sb.AppendLine("        <div class=\"health-label\">HTTP Requests</div>");
            sb.AppendLine($"        <div class=\"health-delta\">{requestsDelta}</div>");
            sb.AppendLine("      </div>");
        }

        sb.AppendLine("    </div>");
        sb.AppendLine("  </div>");
    }

    private static string GetHealthClass(double value, double greenMax, double yellowMax, double redThreshold)
    {
        if (value <= greenMax) return "health-green";
        if (value <= yellowMax) return "health-yellow";
        return "health-red";
    }

    private static string FormatDelta(double baseline, double target, string unit = "")
    {
        var delta = target - baseline;
        if (Math.Abs(delta) < 0.01) return "unchanged";

        var sign = delta > 0 ? "+" : "";
        var arrow = delta > 0 ? "▲" : "▼";
        return $"{arrow} {sign}{delta:N1}{unit} from baseline";
    }

    private static void AppendVerdictSection(StringBuilder sb, RunComparison comparison)
    {
        var result = comparison.Result;
        var verdictClass = result.Verdict switch
        {
            ComparisonVerdict.Improved or ComparisonVerdict.MostlyImproved => "improved",
            ComparisonVerdict.Regressed or ComparisonVerdict.MostlyRegressed => "regressed",
            _ => "mixed"
        };
        var verdictText = result.Verdict switch
        {
            ComparisonVerdict.Improved => "IMPROVED",
            ComparisonVerdict.MostlyImproved => "MOSTLY IMPROVED",
            ComparisonVerdict.Mixed => "MIXED RESULTS",
            ComparisonVerdict.MostlyRegressed => "MOSTLY REGRESSED",
            ComparisonVerdict.Regressed => "REGRESSED",
            _ => "UNKNOWN"
        };

        sb.AppendLine("  <div class=\"verdict\">");
        sb.AppendLine($"    <div class=\"verdict-status {verdictClass}\">{verdictText}</div>");
        sb.AppendLine("    <div class=\"verdict-details\">");
        sb.AppendLine($"      <div class=\"verdict-stat\"><span class=\"verdict-stat-value improved\">{result.ImprovedCount}</span><span class=\"verdict-stat-label\">Improved</span></div>");
        sb.AppendLine($"      <div class=\"verdict-stat\"><span class=\"verdict-stat-value regressed\">{result.RegressedCount}</span><span class=\"verdict-stat-label\">Regressed</span></div>");
        sb.AppendLine($"      <div class=\"verdict-stat\"><span class=\"verdict-stat-value neutral\">{result.NeutralCount}</span><span class=\"verdict-stat-label\">Unchanged</span></div>");
        sb.AppendLine("    </div>");

        // Key changes
        if (result.KeyImprovements.Count > 0 || result.KeyRegressions.Count > 0)
        {
            sb.AppendLine("    <div class=\"key-changes\">");

            if (result.KeyImprovements.Count > 0)
            {
                sb.AppendLine("      <div class=\"key-changes-list improvements\">");
                sb.AppendLine("        <h3>Key Improvements</h3>");
                sb.AppendLine("        <ul>");
                foreach (var item in result.KeyImprovements.Take(5))
                {
                    sb.AppendLine($"          <li class=\"improved\">{HtmlEncode(item)}</li>");
                }
                sb.AppendLine("        </ul>");
                sb.AppendLine("      </div>");
            }

            if (result.KeyRegressions.Count > 0)
            {
                sb.AppendLine("      <div class=\"key-changes-list regressions\">");
                sb.AppendLine("        <h3>Watch</h3>");
                sb.AppendLine("        <ul>");
                foreach (var item in result.KeyRegressions.Take(5))
                {
                    sb.AppendLine($"          <li class=\"regressed\">{HtmlEncode(item)}</li>");
                }
                sb.AppendLine("        </ul>");
                sb.AppendLine("      </div>");
            }

            sb.AppendLine("    </div>");
        }

        sb.AppendLine("  </div>");
    }

    private static void AppendSystemComparison(StringBuilder sb, RunComparison comparison)
    {
        var sys = comparison.Result.System;

        sb.AppendLine("  <div class=\"section\">");
        sb.AppendLine("    <h2>System Metrics</h2>");
        sb.AppendLine("    <table>");
        sb.AppendLine("      <thead><tr><th>Metric</th><th>Baseline</th><th>Target</th><th>Delta</th><th>Change</th></tr></thead>");
        sb.AppendLine("      <tbody>");

        AppendComparisonRowWithDelta(sb, sys.CpuAvg, "%");
        AppendComparisonRowWithDelta(sb, sys.CpuP95, "%");
        AppendComparisonRowWithDelta(sb, sys.CpuMax, "%");
        AppendComparisonRowWithDelta(sb, sys.MemoryAvgMb, " MB");
        AppendComparisonRowWithDelta(sb, sys.MemoryMaxMb, " MB");
        AppendComparisonRowWithDelta(sb, sys.DiskReadAvg, " B/s", formatBytes: true);
        AppendComparisonRowWithDelta(sb, sys.DiskWriteAvg, " B/s", formatBytes: true);
        AppendComparisonRowWithDelta(sb, sys.NetSentAvg, " B/s", formatBytes: true);
        AppendComparisonRowWithDelta(sb, sys.NetReceivedAvg, " B/s", formatBytes: true);

        sb.AppendLine("      </tbody>");
        sb.AppendLine("    </table>");
        sb.AppendLine("  </div>");
    }

    private static void AppendProcessComparison(StringBuilder sb, RunComparison comparison)
    {
        var processes = comparison.Result.Processes;
        if (processes.Count == 0) return;

        sb.AppendLine("  <div class=\"section\">");
        sb.AppendLine("    <h2>Process Metrics</h2>");

        // Show new/removed processes first
        foreach (var proc in processes.Where(p => p.Status != ComparisonStatus.Present))
        {
            var statusText = proc.Status == ComparisonStatus.New ? "(new in target)" : "(removed)";
            var statusClass = proc.Status == ComparisonStatus.New ? "status-new" : "status-removed";
            sb.AppendLine($"    <p class=\"{statusClass}\">{HtmlEncode(proc.ProcessName)} {statusText}</p>");
        }

        // Detail per process
        foreach (var proc in processes.Where(p => p.Status == ComparisonStatus.Present))
        {
            sb.AppendLine($"    <div class=\"process-section\">");
            sb.AppendLine($"      <h3>{HtmlEncode(proc.ProcessName)}</h3>");
            sb.AppendLine("      <table>");
            sb.AppendLine("        <thead><tr><th>Metric</th><th>Baseline</th><th>Target</th><th>Delta</th><th>Change</th></tr></thead>");
            sb.AppendLine("        <tbody>");

            // CPU metrics
            if (proc.CpuAvg != null) AppendComparisonRowWithDelta(sb, proc.CpuAvg, "%");
            if (proc.CpuP95 != null) AppendComparisonRowWithDelta(sb, proc.CpuP95, "%");
            if (proc.CpuMax != null) AppendComparisonRowWithDelta(sb, proc.CpuMax, "%");

            // Memory metrics
            if (proc.MemoryAvgMb != null) AppendComparisonRowWithDelta(sb, proc.MemoryAvgMb, " MB");
            if (proc.MemoryP95Mb != null) AppendComparisonRowWithDelta(sb, proc.MemoryP95Mb, " MB");
            if (proc.MemoryMaxMb != null) AppendComparisonRowWithDelta(sb, proc.MemoryMaxMb, " MB");

            // Additional metrics
            if (proc.PrivateBytesAvgMb != null) AppendComparisonRowWithDelta(sb, proc.PrivateBytesAvgMb, " MB");
            if (proc.ThreadCountAvg != null) AppendComparisonRowWithDelta(sb, proc.ThreadCountAvg, "");
            if (proc.HandleCountAvg != null) AppendComparisonRowWithDelta(sb, proc.HandleCountAvg, "");

            sb.AppendLine("        </tbody>");
            sb.AppendLine("      </table>");
            sb.AppendLine("    </div>");
        }

        sb.AppendLine("  </div>");
    }

    private static void AppendDotNetComparison(StringBuilder sb, RunComparison comparison)
    {
        var apps = comparison.Result.DotNetApps;
        if (apps.Count == 0) return;

        sb.AppendLine("  <div class=\"section\">");
        sb.AppendLine("    <h2>.NET Runtime Metrics</h2>");

        // Exception alert for all apps combined
        var totalExceptions = apps
            .Where(a => a.Status == ComparisonStatus.Present && a.Exceptions != null)
            .Sum(a => a.Exceptions!.TargetValue);
        var baselineExceptions = apps
            .Where(a => a.Status == ComparisonStatus.Present && a.Exceptions != null)
            .Sum(a => a.Exceptions!.BaselineValue);

        if (totalExceptions > 0 || baselineExceptions > 0)
        {
            var exceptionClass = GetHealthClass(totalExceptions, 0, 10, 100);
            var delta = totalExceptions - baselineExceptions;
            var deltaSign = delta > 0 ? "+" : "";
            sb.AppendLine($"    <div class=\"exception-alert {exceptionClass}\">");
            sb.AppendLine($"      <span class=\"exception-count\">{totalExceptions:N0}</span>");
            sb.AppendLine("      <div>");
            sb.AppendLine("        <div class=\"exception-label\">Total Exceptions</div>");
            if (Math.Abs(delta) >= 1)
            {
                var changeClass = delta > 0 ? "regressed" : "improved";
                sb.AppendLine($"        <div class=\"exception-change {changeClass}\">{deltaSign}{delta:N0} from baseline ({baselineExceptions:N0})</div>");
            }
            else
            {
                sb.AppendLine($"        <div class=\"exception-change\">unchanged from baseline</div>");
            }
            sb.AppendLine("      </div>");
            sb.AppendLine("    </div>");
        }

        // Per-app metrics table
        sb.AppendLine("    <table>");
        sb.AppendLine("      <thead><tr><th>App</th><th>Metric</th><th>Baseline</th><th>Target</th><th>Delta</th><th>Change</th></tr></thead>");
        sb.AppendLine("      <tbody>");

        foreach (var app in apps)
        {
            if (app.Status != ComparisonStatus.Present) continue;

            // Memory & GC
            if (app.HeapSizeAvgMb != null)
                AppendProcessRowWithDelta(sb, app.AppName, app.HeapSizeAvgMb, " MB");
            if (app.GcTimeAvg != null)
                AppendProcessRowWithDelta(sb, "", app.GcTimeAvg, "%");
            if (app.Gen0Collections != null)
                AppendProcessRowWithDelta(sb, "", app.Gen0Collections, "");
            if (app.Gen1Collections != null)
                AppendProcessRowWithDelta(sb, "", app.Gen1Collections, "");
            if (app.Gen2Collections != null)
                AppendProcessRowWithDelta(sb, "", app.Gen2Collections, "");

            // Threading
            if (app.ThreadPoolAvg != null)
                AppendProcessRowWithDelta(sb, "", app.ThreadPoolAvg, "");
            if (app.ThreadPoolQueueAvg != null)
                AppendProcessRowWithDelta(sb, "", app.ThreadPoolQueueAvg, "");

            // Exceptions
            if (app.Exceptions != null)
                AppendProcessRowWithDelta(sb, "", app.Exceptions, "");
        }

        sb.AppendLine("      </tbody>");
        sb.AppendLine("    </table>");
        sb.AppendLine("  </div>");
    }

    private static void AppendHttpComparison(StringBuilder sb, RunComparison comparison)
    {
        var endpoints = comparison.Result.HttpEndpoints;
        if (endpoints.Count == 0) return;

        sb.AppendLine("  <div class=\"section\">");
        sb.AppendLine("    <h2>HTTP Endpoints</h2>");

        // Summary stats
        var totalRequests = endpoints.Sum(e => e.TargetRequests);
        var totalErrors = endpoints.Sum(e => e.Target4xx + e.Target5xx);
        var baselineRequests = endpoints.Sum(e => e.BaselineRequests);
        var baselineErrors = endpoints.Sum(e => e.Baseline4xx + e.Baseline5xx);

        var errorRate = totalRequests > 0 ? (double)totalErrors / totalRequests * 100 : 0;
        var baselineErrorRate = baselineRequests > 0 ? (double)baselineErrors / baselineRequests * 100 : 0;
        var errorClass = GetHealthClass(errorRate, 1, 5, 10);

        if (totalRequests > 0 || baselineRequests > 0)
        {
            sb.AppendLine("    <div style=\"display: flex; gap: 2rem; margin-bottom: 1rem;\">");
            sb.AppendLine($"      <div><strong>{totalRequests:N0}</strong> total requests (baseline: {baselineRequests:N0})</div>");
            sb.AppendLine($"      <div class=\"{errorClass}\"><strong>{errorRate:N1}%</strong> error rate (baseline: {baselineErrorRate:N1}%)</div>");
            sb.AppendLine("    </div>");
        }

        sb.AppendLine("    <table>");
        sb.AppendLine("      <thead><tr><th>Endpoint</th><th>Requests</th><th>Avg Latency</th><th>P95 Latency</th><th>Errors</th><th>Change</th></tr></thead>");
        sb.AppendLine("      <tbody>");

        foreach (var ep in endpoints.Take(20))
        {
            if (ep.Status == ComparisonStatus.New)
            {
                var newErrors = ep.Target4xx + ep.Target5xx;
                var targetAvg = ep.LatencyAvg?.TargetValue ?? 0;
                sb.AppendLine($"        <tr>");
                sb.AppendLine($"          <td class=\"status-new\">{HtmlEncode(ep.Endpoint)} (new)</td>");
                sb.AppendLine($"          <td class=\"number\">{ep.TargetRequests:N0}</td>");
                sb.AppendLine($"          <td class=\"number\">{targetAvg:N1} ms</td>");
                sb.AppendLine($"          <td class=\"number\">{ep.TargetLatencyP95:N1} ms</td>");
                sb.AppendLine($"          <td class=\"number\">{newErrors:N0}</td>");
                sb.AppendLine($"          <td>—</td>");
                sb.AppendLine($"        </tr>");
                continue;
            }
            if (ep.Status == ComparisonStatus.Removed)
            {
                var removedErrors = ep.Baseline4xx + ep.Baseline5xx;
                var baselineAvg = ep.LatencyAvg?.BaselineValue ?? 0;
                sb.AppendLine($"        <tr>");
                sb.AppendLine($"          <td class=\"status-removed\">{HtmlEncode(ep.Endpoint)}</td>");
                sb.AppendLine($"          <td class=\"number status-removed\">{ep.BaselineRequests:N0}</td>");
                sb.AppendLine($"          <td class=\"number status-removed\">{baselineAvg:N1} ms</td>");
                sb.AppendLine($"          <td class=\"number status-removed\">{ep.BaselineLatencyP95:N1} ms</td>");
                sb.AppendLine($"          <td class=\"number status-removed\">{removedErrors:N0}</td>");
                sb.AppendLine($"          <td>removed</td>");
                sb.AppendLine($"        </tr>");
                continue;
            }

            var changeClass = ep.LatencyP95?.Direction switch
            {
                ChangeDirection.Improved => "improved",
                ChangeDirection.Regressed => "regressed",
                _ => "neutral"
            };
            var changeText = ep.LatencyP95?.FormatChange() ?? "—";
            var changeIcon = ep.LatencyP95?.GetIcon() ?? "";

            var baselineErrs = ep.Baseline4xx + ep.Baseline5xx;
            var targetErrs = ep.Target4xx + ep.Target5xx;
            var errDelta = targetErrs - baselineErrs;
            var errClass = errDelta > 0 ? "regressed" : (errDelta < 0 ? "improved" : "neutral");
            var errDisplay = errDelta != 0 ? $"{baselineErrs} → {targetErrs}" : $"{targetErrs}";

            var baselineAvgLatency = ep.LatencyAvg?.BaselineValue ?? 0;
            var targetAvgLatency = ep.LatencyAvg?.TargetValue ?? 0;

            sb.AppendLine($"        <tr>");
            sb.AppendLine($"          <td>{HtmlEncode(ep.Endpoint)}</td>");
            sb.AppendLine($"          <td class=\"number\">{ep.BaselineRequests:N0} → {ep.TargetRequests:N0}</td>");
            sb.AppendLine($"          <td class=\"number\">{baselineAvgLatency:N1} → {targetAvgLatency:N1} ms</td>");
            sb.AppendLine($"          <td class=\"number\">{ep.BaselineLatencyP95:N1} → {ep.TargetLatencyP95:N1} ms</td>");
            sb.AppendLine($"          <td class=\"number {errClass}\">{errDisplay}</td>");
            sb.AppendLine($"          <td class=\"{changeClass}\">{changeText} {changeIcon}</td>");
            sb.AppendLine($"        </tr>");
        }

        sb.AppendLine("      </tbody>");
        sb.AppendLine("    </table>");
        sb.AppendLine("  </div>");
    }

    private static void AppendSqlComparison(StringBuilder sb, RunComparison comparison)
    {
        var sql = comparison.Result.Sql;
        if (sql == null) return;

        sb.AppendLine("  <div class=\"section\">");
        sb.AppendLine("    <h2>SQL Server Metrics</h2>");

        // Blocked requests alert
        if (sql.BlockedRequestsAvg.TargetValue > 0 || sql.BlockedRequestsAvg.BaselineValue > 0)
        {
            var blockedClass = GetHealthClass(sql.BlockedRequestsAvg.TargetValue, 0, 1, 5);
            var delta = sql.BlockedRequestsAvg.TargetValue - sql.BlockedRequestsAvg.BaselineValue;
            var deltaSign = delta > 0 ? "+" : "";
            sb.AppendLine($"    <div class=\"exception-alert {blockedClass}\">");
            sb.AppendLine($"      <span class=\"exception-count\">{sql.BlockedRequestsAvg.TargetValue:N1}</span>");
            sb.AppendLine("      <div>");
            sb.AppendLine("        <div class=\"exception-label\">Avg Blocked Requests</div>");
            if (Math.Abs(delta) >= 0.1)
            {
                var changeClass = delta > 0 ? "regressed" : "improved";
                sb.AppendLine($"        <div class=\"exception-change {changeClass}\">{deltaSign}{delta:N1} from baseline ({sql.BlockedRequestsAvg.BaselineValue:N1})</div>");
            }
            else
            {
                sb.AppendLine($"        <div class=\"exception-change\">unchanged from baseline</div>");
            }
            sb.AppendLine("      </div>");
            sb.AppendLine("    </div>");
        }

        sb.AppendLine("    <table>");
        sb.AppendLine("      <thead><tr><th>Metric</th><th>Baseline</th><th>Target</th><th>Delta</th><th>Change</th></tr></thead>");
        sb.AppendLine("      <tbody>");

        AppendComparisonRowWithDelta(sb, sql.ActiveRequestsAvg, "");
        AppendComparisonRowWithDelta(sb, sql.BlockedRequestsAvg, "");
        AppendComparisonRowWithDelta(sb, sql.WaitTimeAvg, " ms");
        AppendComparisonRowWithDelta(sb, sql.ConnectionsAvg, "");

        sb.AppendLine("      </tbody>");
        sb.AppendLine("    </table>");
        sb.AppendLine("  </div>");
    }

    private static void AppendOverlayCharts(StringBuilder sb, RunComparison comparison, ReportData baselineData, ReportData targetData)
    {
        sb.AppendLine("  <div class=\"section\">");
        sb.AppendLine("    <h2>Time-Series Comparison</h2>");
        sb.AppendLine("    <div class=\"chart-legend\">");
        sb.AppendLine("      <div class=\"legend-item\"><div class=\"legend-color legend-baseline\"></div>Baseline</div>");
        sb.AppendLine("      <div class=\"legend-item\"><div class=\"legend-color legend-target\"></div>Target</div>");
        sb.AppendLine("    </div>");

        // CPU chart
        sb.AppendLine("    <div class=\"chart-container\"><canvas id=\"cpuChart\"></canvas></div>");

        // Memory chart
        sb.AppendLine("    <div class=\"chart-container\"><canvas id=\"memoryChart\"></canvas></div>");

        // Disk I/O chart
        sb.AppendLine("    <div class=\"chart-container\"><canvas id=\"diskChart\"></canvas></div>");

        // Network chart
        sb.AppendLine("    <div class=\"chart-container\"><canvas id=\"networkChart\"></canvas></div>");

        sb.AppendLine("  </div>");

        // Chart scripts
        sb.AppendLine("<script>");

        // Prepare data series
        var baselineSamples = baselineData.SystemSamples.OrderBy(s => s.TimestampUtc).ToList();
        var targetSamples = targetData.SystemSamples.OrderBy(s => s.TimestampUtc).ToList();

        var baselineCpu = baselineSamples.Select((s, i) => new { x = i, y = s.CpuTotalPercent ?? 0 }).ToList();
        var targetCpu = targetSamples.Select((s, i) => new { x = i, y = s.CpuTotalPercent ?? 0 }).ToList();

        var baselineMemory = baselineSamples.Select((s, i) => new { x = i, y = s.MemoryInUseMb ?? 0 }).ToList();
        var targetMemory = targetSamples.Select((s, i) => new { x = i, y = s.MemoryInUseMb ?? 0 }).ToList();

        var baselineDiskRead = baselineSamples.Select((s, i) => new { x = i, y = (s.DiskReadBytesPerSec ?? 0) / 1_000_000.0 }).ToList();
        var targetDiskRead = targetSamples.Select((s, i) => new { x = i, y = (s.DiskReadBytesPerSec ?? 0) / 1_000_000.0 }).ToList();
        var baselineDiskWrite = baselineSamples.Select((s, i) => new { x = i, y = (s.DiskWriteBytesPerSec ?? 0) / 1_000_000.0 }).ToList();
        var targetDiskWrite = targetSamples.Select((s, i) => new { x = i, y = (s.DiskWriteBytesPerSec ?? 0) / 1_000_000.0 }).ToList();

        var baselineNetSent = baselineSamples.Select((s, i) => new { x = i, y = (s.NetBytesSentPerSec ?? 0) / 1_000_000.0 }).ToList();
        var targetNetSent = targetSamples.Select((s, i) => new { x = i, y = (s.NetBytesSentPerSec ?? 0) / 1_000_000.0 }).ToList();
        var baselineNetRecv = baselineSamples.Select((s, i) => new { x = i, y = (s.NetBytesReceivedPerSec ?? 0) / 1_000_000.0 }).ToList();
        var targetNetRecv = targetSamples.Select((s, i) => new { x = i, y = (s.NetBytesReceivedPerSec ?? 0) / 1_000_000.0 }).ToList();

        // CPU Chart
        sb.AppendLine("new Chart(document.getElementById('cpuChart'), {");
        sb.AppendLine("  type: 'line',");
        sb.AppendLine("  data: {");
        sb.AppendLine("    datasets: [");
        sb.AppendLine("      {");
        sb.AppendLine("        label: 'Baseline CPU %',");
        sb.AppendLine($"        data: [{string.Join(",", baselineCpu.Select(p => $"{{x:{p.x},y:{p.y:F1}}}"))}],");
        sb.AppendLine("        borderColor: '#8da0cb',");
        sb.AppendLine("        fill: false,");
        sb.AppendLine("        tension: 0.3,");
        sb.AppendLine("        pointRadius: 0");
        sb.AppendLine("      },");
        sb.AppendLine("      {");
        sb.AppendLine("        label: 'Target CPU %',");
        sb.AppendLine($"        data: [{string.Join(",", targetCpu.Select(p => $"{{x:{p.x},y:{p.y:F1}}}"))}],");
        sb.AppendLine("        borderColor: '#66c2a5',");
        sb.AppendLine("        fill: false,");
        sb.AppendLine("        tension: 0.3,");
        sb.AppendLine("        pointRadius: 0");
        sb.AppendLine("      }");
        sb.AppendLine("    ]");
        sb.AppendLine("  },");
        sb.AppendLine("  options: {");
        sb.AppendLine("    responsive: true,");
        sb.AppendLine("    maintainAspectRatio: false,");
        sb.AppendLine("    plugins: { legend: { display: false }, title: { display: true, text: 'CPU Usage Over Time', color: '#e8e8e8' } },");
        sb.AppendLine("    scales: {");
        sb.AppendLine("      x: { type: 'linear', title: { display: true, text: 'Sample #', color: '#a0a0a0' }, grid: { color: '#4a4a4a' }, ticks: { color: '#a0a0a0' } },");
        sb.AppendLine("      y: { min: 0, max: 100, title: { display: true, text: 'CPU %', color: '#a0a0a0' }, grid: { color: '#4a4a4a' }, ticks: { color: '#a0a0a0' } }");
        sb.AppendLine("    }");
        sb.AppendLine("  }");
        sb.AppendLine("});");

        // Memory Chart
        sb.AppendLine("new Chart(document.getElementById('memoryChart'), {");
        sb.AppendLine("  type: 'line',");
        sb.AppendLine("  data: {");
        sb.AppendLine("    datasets: [");
        sb.AppendLine("      {");
        sb.AppendLine("        label: 'Baseline Memory MB',");
        sb.AppendLine($"        data: [{string.Join(",", baselineMemory.Select(p => $"{{x:{p.x},y:{p.y:F0}}}"))}],");
        sb.AppendLine("        borderColor: '#8da0cb',");
        sb.AppendLine("        fill: false,");
        sb.AppendLine("        tension: 0.3,");
        sb.AppendLine("        pointRadius: 0");
        sb.AppendLine("      },");
        sb.AppendLine("      {");
        sb.AppendLine("        label: 'Target Memory MB',");
        sb.AppendLine($"        data: [{string.Join(",", targetMemory.Select(p => $"{{x:{p.x},y:{p.y:F0}}}"))}],");
        sb.AppendLine("        borderColor: '#66c2a5',");
        sb.AppendLine("        fill: false,");
        sb.AppendLine("        tension: 0.3,");
        sb.AppendLine("        pointRadius: 0");
        sb.AppendLine("      }");
        sb.AppendLine("    ]");
        sb.AppendLine("  },");
        sb.AppendLine("  options: {");
        sb.AppendLine("    responsive: true,");
        sb.AppendLine("    maintainAspectRatio: false,");
        sb.AppendLine("    plugins: { legend: { display: false }, title: { display: true, text: 'Memory Usage Over Time', color: '#e8e8e8' } },");
        sb.AppendLine("    scales: {");
        sb.AppendLine("      x: { type: 'linear', title: { display: true, text: 'Sample #', color: '#a0a0a0' }, grid: { color: '#4a4a4a' }, ticks: { color: '#a0a0a0' } },");
        sb.AppendLine("      y: { title: { display: true, text: 'Memory (MB)', color: '#a0a0a0' }, grid: { color: '#4a4a4a' }, ticks: { color: '#a0a0a0' } }");
        sb.AppendLine("    }");
        sb.AppendLine("  }");
        sb.AppendLine("});");

        // Disk I/O Chart (shows read as solid, write as dashed)
        sb.AppendLine("new Chart(document.getElementById('diskChart'), {");
        sb.AppendLine("  type: 'line',");
        sb.AppendLine("  data: {");
        sb.AppendLine("    datasets: [");
        sb.AppendLine("      {");
        sb.AppendLine("        label: 'Baseline Read',");
        sb.AppendLine($"        data: [{string.Join(",", baselineDiskRead.Select(p => $"{{x:{p.x},y:{p.y:F2}}}"))}],");
        sb.AppendLine("        borderColor: '#8da0cb',");
        sb.AppendLine("        fill: false,");
        sb.AppendLine("        tension: 0.3,");
        sb.AppendLine("        pointRadius: 0");
        sb.AppendLine("      },");
        sb.AppendLine("      {");
        sb.AppendLine("        label: 'Target Read',");
        sb.AppendLine($"        data: [{string.Join(",", targetDiskRead.Select(p => $"{{x:{p.x},y:{p.y:F2}}}"))}],");
        sb.AppendLine("        borderColor: '#66c2a5',");
        sb.AppendLine("        fill: false,");
        sb.AppendLine("        tension: 0.3,");
        sb.AppendLine("        pointRadius: 0");
        sb.AppendLine("      },");
        sb.AppendLine("      {");
        sb.AppendLine("        label: 'Baseline Write',");
        sb.AppendLine($"        data: [{string.Join(",", baselineDiskWrite.Select(p => $"{{x:{p.x},y:{p.y:F2}}}"))}],");
        sb.AppendLine("        borderColor: '#8da0cb',");
        sb.AppendLine("        borderDash: [5, 5],");
        sb.AppendLine("        fill: false,");
        sb.AppendLine("        tension: 0.3,");
        sb.AppendLine("        pointRadius: 0");
        sb.AppendLine("      },");
        sb.AppendLine("      {");
        sb.AppendLine("        label: 'Target Write',");
        sb.AppendLine($"        data: [{string.Join(",", targetDiskWrite.Select(p => $"{{x:{p.x},y:{p.y:F2}}}"))}],");
        sb.AppendLine("        borderColor: '#66c2a5',");
        sb.AppendLine("        borderDash: [5, 5],");
        sb.AppendLine("        fill: false,");
        sb.AppendLine("        tension: 0.3,");
        sb.AppendLine("        pointRadius: 0");
        sb.AppendLine("      }");
        sb.AppendLine("    ]");
        sb.AppendLine("  },");
        sb.AppendLine("  options: {");
        sb.AppendLine("    responsive: true,");
        sb.AppendLine("    maintainAspectRatio: false,");
        sb.AppendLine("    plugins: { legend: { display: true, labels: { color: '#a0a0a0' } }, title: { display: true, text: 'Disk I/O Over Time (solid=read, dashed=write)', color: '#e8e8e8' } },");
        sb.AppendLine("    scales: {");
        sb.AppendLine("      x: { type: 'linear', title: { display: true, text: 'Sample #', color: '#a0a0a0' }, grid: { color: '#4a4a4a' }, ticks: { color: '#a0a0a0' } },");
        sb.AppendLine("      y: { title: { display: true, text: 'MB/s', color: '#a0a0a0' }, grid: { color: '#4a4a4a' }, ticks: { color: '#a0a0a0' } }");
        sb.AppendLine("    }");
        sb.AppendLine("  }");
        sb.AppendLine("});");

        // Network Chart (shows sent as solid, received as dashed)
        sb.AppendLine("new Chart(document.getElementById('networkChart'), {");
        sb.AppendLine("  type: 'line',");
        sb.AppendLine("  data: {");
        sb.AppendLine("    datasets: [");
        sb.AppendLine("      {");
        sb.AppendLine("        label: 'Baseline Sent',");
        sb.AppendLine($"        data: [{string.Join(",", baselineNetSent.Select(p => $"{{x:{p.x},y:{p.y:F2}}}"))}],");
        sb.AppendLine("        borderColor: '#8da0cb',");
        sb.AppendLine("        fill: false,");
        sb.AppendLine("        tension: 0.3,");
        sb.AppendLine("        pointRadius: 0");
        sb.AppendLine("      },");
        sb.AppendLine("      {");
        sb.AppendLine("        label: 'Target Sent',");
        sb.AppendLine($"        data: [{string.Join(",", targetNetSent.Select(p => $"{{x:{p.x},y:{p.y:F2}}}"))}],");
        sb.AppendLine("        borderColor: '#66c2a5',");
        sb.AppendLine("        fill: false,");
        sb.AppendLine("        tension: 0.3,");
        sb.AppendLine("        pointRadius: 0");
        sb.AppendLine("      },");
        sb.AppendLine("      {");
        sb.AppendLine("        label: 'Baseline Received',");
        sb.AppendLine($"        data: [{string.Join(",", baselineNetRecv.Select(p => $"{{x:{p.x},y:{p.y:F2}}}"))}],");
        sb.AppendLine("        borderColor: '#8da0cb',");
        sb.AppendLine("        borderDash: [5, 5],");
        sb.AppendLine("        fill: false,");
        sb.AppendLine("        tension: 0.3,");
        sb.AppendLine("        pointRadius: 0");
        sb.AppendLine("      },");
        sb.AppendLine("      {");
        sb.AppendLine("        label: 'Target Received',");
        sb.AppendLine($"        data: [{string.Join(",", targetNetRecv.Select(p => $"{{x:{p.x},y:{p.y:F2}}}"))}],");
        sb.AppendLine("        borderColor: '#66c2a5',");
        sb.AppendLine("        borderDash: [5, 5],");
        sb.AppendLine("        fill: false,");
        sb.AppendLine("        tension: 0.3,");
        sb.AppendLine("        pointRadius: 0");
        sb.AppendLine("      }");
        sb.AppendLine("    ]");
        sb.AppendLine("  },");
        sb.AppendLine("  options: {");
        sb.AppendLine("    responsive: true,");
        sb.AppendLine("    maintainAspectRatio: false,");
        sb.AppendLine("    plugins: { legend: { display: true, labels: { color: '#a0a0a0' } }, title: { display: true, text: 'Network I/O Over Time (solid=sent, dashed=received)', color: '#e8e8e8' } },");
        sb.AppendLine("    scales: {");
        sb.AppendLine("      x: { type: 'linear', title: { display: true, text: 'Sample #', color: '#a0a0a0' }, grid: { color: '#4a4a4a' }, ticks: { color: '#a0a0a0' } },");
        sb.AppendLine("      y: { title: { display: true, text: 'MB/s', color: '#a0a0a0' }, grid: { color: '#4a4a4a' }, ticks: { color: '#a0a0a0' } }");
        sb.AppendLine("    }");
        sb.AppendLine("  }");
        sb.AppendLine("});");

        sb.AppendLine("</script>");
    }

    private static void AppendFooter(StringBuilder sb)
    {
        sb.AppendLine("  <div class=\"footer\">");
        sb.AppendLine($"    <p>Generated by Blackpaw on {DateTime.Now:yyyy-MM-dd HH:mm}</p>");
        sb.AppendLine("    <p class=\"memorial\">In memory of Oliver</p>");
        sb.AppendLine("  </div>");
        sb.AppendLine("</div>");
    }

    private static void AppendComparisonRow(StringBuilder sb, MetricComparison metric, string unit, bool formatBytes = false)
    {
        var baselineValue = formatBytes ? FormatBytes(metric.BaselineValue) : $"{metric.BaselineValue:N1}{unit}";
        var targetValue = formatBytes ? FormatBytes(metric.TargetValue) : $"{metric.TargetValue:N1}{unit}";
        var changeClass = metric.Direction switch
        {
            ChangeDirection.Improved => "improved",
            ChangeDirection.Regressed => "regressed",
            _ => "neutral"
        };
        var changeText = metric.FormatChange();
        var changeIcon = metric.GetIcon();

        sb.AppendLine($"        <tr>");
        sb.AppendLine($"          <td>{HtmlEncode(metric.Name)}</td>");
        sb.AppendLine($"          <td class=\"number\">{baselineValue}</td>");
        sb.AppendLine($"          <td class=\"number\">{targetValue}</td>");
        sb.AppendLine($"          <td class=\"{changeClass}\">{changeText} {changeIcon}</td>");
        sb.AppendLine($"        </tr>");
    }

    private static void AppendComparisonRowWithDelta(StringBuilder sb, MetricComparison metric, string unit, bool formatBytes = false)
    {
        var baselineValue = formatBytes ? FormatBytes(metric.BaselineValue) : $"{metric.BaselineValue:N1}{unit}";
        var targetValue = formatBytes ? FormatBytes(metric.TargetValue) : $"{metric.TargetValue:N1}{unit}";

        // Calculate absolute delta
        var delta = metric.TargetValue - metric.BaselineValue;
        var deltaSign = delta >= 0 ? "+" : "";
        var deltaValue = formatBytes
            ? (delta >= 0 ? "+" : "") + FormatBytes(Math.Abs(delta)).Replace("/s", "")
            : $"{deltaSign}{delta:N1}{unit}";
        if (Math.Abs(delta) < 0.01) deltaValue = "—";

        var changeClass = metric.Direction switch
        {
            ChangeDirection.Improved => "improved",
            ChangeDirection.Regressed => "regressed",
            _ => "neutral"
        };
        var changeText = metric.FormatChange();
        var changeIcon = metric.GetIcon();

        sb.AppendLine($"        <tr>");
        sb.AppendLine($"          <td>{HtmlEncode(metric.Name)}</td>");
        sb.AppendLine($"          <td class=\"number\">{baselineValue}</td>");
        sb.AppendLine($"          <td class=\"number\">{targetValue}</td>");
        sb.AppendLine($"          <td class=\"number\">{deltaValue}</td>");
        sb.AppendLine($"          <td class=\"{changeClass}\">{changeText} {changeIcon}</td>");
        sb.AppendLine($"        </tr>");
    }

    private static void AppendProcessRow(StringBuilder sb, string processName, MetricComparison metric, string unit)
    {
        var changeClass = metric.Direction switch
        {
            ChangeDirection.Improved => "improved",
            ChangeDirection.Regressed => "regressed",
            _ => "neutral"
        };
        var changeText = metric.FormatChange();
        var changeIcon = metric.GetIcon();

        sb.AppendLine($"        <tr>");
        sb.AppendLine($"          <td>{HtmlEncode(processName)}</td>");
        sb.AppendLine($"          <td>{HtmlEncode(metric.Name)}</td>");
        sb.AppendLine($"          <td class=\"number\">{metric.BaselineValue:N1}{unit}</td>");
        sb.AppendLine($"          <td class=\"number\">{metric.TargetValue:N1}{unit}</td>");
        sb.AppendLine($"          <td class=\"{changeClass}\">{changeText} {changeIcon}</td>");
        sb.AppendLine($"        </tr>");
    }

    private static void AppendProcessRowWithDelta(StringBuilder sb, string processName, MetricComparison metric, string unit)
    {
        var delta = metric.TargetValue - metric.BaselineValue;
        var deltaSign = delta >= 0 ? "+" : "";
        var deltaValue = $"{deltaSign}{delta:N1}{unit}";
        if (Math.Abs(delta) < 0.01) deltaValue = "—";

        var changeClass = metric.Direction switch
        {
            ChangeDirection.Improved => "improved",
            ChangeDirection.Regressed => "regressed",
            _ => "neutral"
        };
        var changeText = metric.FormatChange();
        var changeIcon = metric.GetIcon();

        sb.AppendLine($"        <tr>");
        sb.AppendLine($"          <td>{HtmlEncode(processName)}</td>");
        sb.AppendLine($"          <td>{HtmlEncode(metric.Name)}</td>");
        sb.AppendLine($"          <td class=\"number\">{metric.BaselineValue:N1}{unit}</td>");
        sb.AppendLine($"          <td class=\"number\">{metric.TargetValue:N1}{unit}</td>");
        sb.AppendLine($"          <td class=\"number\">{deltaValue}</td>");
        sb.AppendLine($"          <td class=\"{changeClass}\">{changeText} {changeIcon}</td>");
        sb.AppendLine($"        </tr>");
    }

    private static string FormatDuration(double? seconds)
    {
        if (!seconds.HasValue) return "—";
        var s = seconds.Value;
        if (s < 60) return $"{s:N0}s";
        if (s < 3600) return $"{s / 60:N0}m {s % 60:N0}s";
        return $"{s / 3600:N0}h {(s % 3600) / 60:N0}m";
    }

    private static string FormatBytes(double bytes)
    {
        return bytes switch
        {
            >= 1_000_000_000 => $"{bytes / 1_000_000_000:N1} GB/s",
            >= 1_000_000 => $"{bytes / 1_000_000:N1} MB/s",
            >= 1_000 => $"{bytes / 1_000:N1} KB/s",
            _ => $"{bytes:N0} B/s"
        };
    }

    private static string HtmlEncode(string text)
    {
        return System.Net.WebUtility.HtmlEncode(text);
    }
}
