using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.Tools;

/// <summary>
/// Queries Log Analytics workspaces and Application Insights via their direct query APIs.
/// Uses a Log Analytics-scoped token (also accepted by App Insights query API).
/// </summary>
public class LogAnalyticsQueryTools
{
    private readonly UserTokens _tokens;

    public LogAnalyticsQueryTools(UserTokens tokens) => _tokens = tokens;

    public IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(QueryLogAnalytics, "QueryLogAnalytics", @"Runs a KQL query against a Log Analytics workspace or Application Insights component.
DATA SCOPING: ALWAYS use summarize, top, take, or where to limit results. Use bin(TimeGenerated, 1d) for time aggregation — never raw per-minute rows. Project only needed columns — never select *. Start with an aggregated overview, then drill down.
LOG ANALYTICS: Specify workspaceId (GUID). APP INSIGHTS: Specify appId (GUID) and set target='appinsights'.
FinOps tables: Perf — VM CPU/memory/disk; InsightsMetrics — VM/container insights; Heartbeat — VM online/offline detection (find idle VMs); ContainerInventory/KubePodInventory — AKS resource requests vs usage; ContainerLog — container stdout/stderr volume (ingestion cost driver); AzureMetrics — PaaS metrics; AzureDiagnostics — App Gateway/SQL/Firewall throughput; AzureActivity — resource lifecycle (cost attribution); AppRequests/AppDependencies — App Insights app telemetry; Usage/_BilledSize — ingestion volume (meta-cost: cost of observability).");
    }

    private async Task<string> QueryLogAnalytics(
        [Description("The workspace GUID (Log Analytics) or app GUID (App Insights)")] string id,
        [Description("KQL query to execute")] string query,
        [Description("Optional timespan, e.g. PT1H, P1D, P7D, P30D. Default: P1D")] string? timespan = "P1D",
        [Description("Target API: 'loganalytics' (default) or 'appinsights'")] string? target = "loganalytics")
    {
        using var activity = HttpHelper.Telemetry.StartActivity("QueryLogAnalytics");
        activity?.SetTag("la.id", id);
        activity?.SetTag("la.target", target);
        activity?.SetTag("la.query", query?.Length > 500 ? query[..500] + "..." : query);
        activity?.SetTag("la.timespan", timespan);

        var token = _tokens.LogAnalyticsToken;
        if (string.IsNullOrEmpty(token))
            return HttpHelper.TokenMissing("LogAnalyticsToken", activity, "la");

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(query))
        {
            activity?.SetTag("la.result", "invalid_input");
            return $"HTTP 400 BadRequest\nMissing required parameters: id='{id}', query='{query}'. Both are required.";
        }

        var isAppInsights = target?.Trim().Equals("appinsights", StringComparison.OrdinalIgnoreCase) == true;
        var baseUrl = isAppInsights
            ? $"https://api.applicationinsights.io/v1/apps/{Uri.EscapeDataString(id)}/query"
            : $"https://api.loganalytics.io/v1/workspaces/{Uri.EscapeDataString(id)}/query";

        var bodyObj = string.IsNullOrWhiteSpace(timespan)
            ? new { query }
            : (object)new { query, timespan };

        return await HttpHelper.SendWithRetryAsync(
            baseUrl, token, activity, "la",
            method: HttpMethod.Post,
            jsonBody: JsonSerializer.Serialize(bodyObj));
    }

}
