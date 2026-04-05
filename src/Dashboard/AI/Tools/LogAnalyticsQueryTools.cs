using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;

using AzureFinOps.Dashboard.Auth;
using AzureFinOps.Dashboard.Infrastructure;

namespace AzureFinOps.Dashboard.AI.Tools;

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
LOG ANALYTICS: Specify workspaceId (GUID) — find it via QueryAzure GET /subscriptions/{id}/providers/Microsoft.OperationalInsights/workspaces?api-version=2025-07-01 (customerId field is the workspace GUID).
APP INSIGHTS: Specify appId (GUID) and set target='appinsights' — find it via Azure portal or QueryAzure.

=== FINOPS TABLES & KQL PATTERNS ===
Perf — VM CPU/memory/disk metrics. Example: Perf | where ObjectName == 'Processor' and CounterName == '% Processor Time' | summarize AvgCPU=avg(CounterValue) by Computer, bin(TimeGenerated, 1d) | where AvgCPU < 5 — find idle VMs (CPU < 5% = candidate for right-sizing/shutdown).
InsightsMetrics — VM Insights and container insights metrics (CPU, memory, disk, network). Example: InsightsMetrics | where Namespace == 'Processor' and Name == 'UtilizationPercentage' | summarize avg(Val) by Computer, bin(TimeGenerated, 1h).
Heartbeat — VM heartbeat signals; gaps indicate offline/deallocated VMs. Example: Heartbeat | summarize LastHeartbeat=max(TimeGenerated) by Computer | where LastHeartbeat < ago(7d) — VMs not seen in 7 days (potentially orphaned but still billed).
ContainerInventory — AKS container inventory with image, state, pod. KubePodInventory — AKS pod details with CPU/memory requests vs limits. Example: KubePodInventory | summarize avg(PodRequests_cpu), avg(PodLimits_cpu) by Namespace, ControllerName — find over-provisioned pods.
ContainerLog — container stdout/stderr log volume; often the #1 ingestion cost driver. Example: ContainerLog | summarize TotalBytes=sum(_BilledSize) by ContainerID | top 10 by TotalBytes desc.
AzureMetrics — PaaS resource metrics (DTU usage for SQL, RU/s for Cosmos DB, request units). Example: AzureMetrics | where ResourceProvider == 'MICROSOFT.SQL' | summarize avg(Average) by MetricName, Resource, bin(TimeGenerated, 1d).
AzureDiagnostics — diagnostic logs from App Gateway, SQL, Firewall, Key Vault. Example: AzureDiagnostics | where ResourceType == 'APPLICATIONGATEWAYS' | summarize count() by OperationName, bin(TimeGenerated, 1h).
AzureActivity — who created/deleted/modified resources (OperationName, Caller, ResourceGroup). Example: AzureActivity | where OperationNameValue endswith 'write' or OperationNameValue endswith 'delete' | summarize count() by Caller, OperationNameValue | top 20 by count_.
AppRequests — Application Insights HTTP requests (url, duration, resultCode, success). AppDependencies — outbound dependency calls (SQL, HTTP, Redis, etc.) with duration.
Usage — Log Analytics ingestion volume per data type. Example: Usage | summarize DataGB=sum(Quantity)/1024 by DataType | top 10 by DataGB desc — find which tables cost the most for ingestion.
_BilledSize — per-record ingestion size column available on all tables; use sum(_BilledSize) for cost attribution.
Update — Windows Update compliance data; UpdateSummary — patch compliance summary.
SecurityEvent — Windows security events (logon, privilege use); SecurityAlert — Defender alerts.
Syslog — Linux syslog messages (Facility, SeverityLevel, SyslogMessage).
W3CIISLog — IIS web server access logs (request URL, status, bytes, client IP).");
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
