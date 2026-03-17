using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.Tools;

/// <summary>
/// Queries Log Analytics workspaces and Application Insights via their direct query APIs.
/// Uses a Log Analytics-scoped token (also accepted by App Insights query API).
/// </summary>
public static class LogAnalyticsQueryTools
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly ActivitySource Telemetry = new("AzureFinOps.AI");
    private const int MaxResponseChars = 80_000;

    public static IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(QueryLogAnalytics, "QueryLogAnalytics", @"Runs a KQL query against a Log Analytics workspace or Application Insights component.
Use for VM/container metrics, network logs, diagnostics, and ingestion cost analysis.
LOG ANALYTICS: Specify workspaceId (GUID). APP INSIGHTS: Specify appId (GUID) and set target='appinsights'.
FinOps-relevant tables: Perf — VM CPU/memory/disk; InsightsMetrics — VM/container insights; ContainerInventory/KubePodInventory — AKS resource requests vs usage; AzureMetrics — PaaS platform metrics; AzureDiagnostics — App Gateway/SQL/Firewall throughput; Usage/_BilledSize — Log Analytics ingestion volume (meta-cost: cost of observability).");
    }

    private static async Task<string> QueryLogAnalytics(
        [Description("The workspace GUID (Log Analytics) or app GUID (App Insights)")] string id,
        [Description("KQL query to execute")] string query,
        [Description("Optional timespan, e.g. PT1H, P1D, P7D, P30D. Default: P1D")] string? timespan = "P1D",
        [Description("Target API: 'loganalytics' (default) or 'appinsights'")] string? target = "loganalytics")
    {
        using var activity = Telemetry.StartActivity("QueryLogAnalytics");
        activity?.SetTag("la.id", id);
        activity?.SetTag("la.target", target);
        activity?.SetTag("la.query", query?.Length > 500 ? query[..500] + "..." : query);
        activity?.SetTag("la.timespan", timespan);

        var token = TokenContext.LogAnalyticsToken;
        if (string.IsNullOrEmpty(token))
        {
            activity?.SetTag("la.result", "not_connected");
            activity?.SetStatus(ActivityStatusCode.Error, "Not connected");
            return "HTTP 401 Unauthorized\nTokenContext.LogAnalyticsToken is null — no Log Analytics token available. The user must click 'Connect Azure' in the sidebar to authenticate, then retry.";
        }

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(query))
        {
            activity?.SetTag("la.result", "invalid_input");
            return $"HTTP 400 BadRequest\nMissing required parameters: id='{id}', query='{query}'. Both are required.";
        }

        var isAppInsights = target?.Trim().Equals("appinsights", StringComparison.OrdinalIgnoreCase) == true;
        var baseUrl = isAppInsights
            ? $"https://api.applicationinsights.io/v1/apps/{Uri.EscapeDataString(id)}/query"
            : $"https://api.loganalytics.io/v1/workspaces/{Uri.EscapeDataString(id)}/query";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("User-Agent", "FinOps-Dashboard/1.0");

            var bodyObj = string.IsNullOrWhiteSpace(timespan)
                ? new { query }
                : (object)new { query, timespan };
            var bodyJson = JsonSerializer.Serialize(bodyObj);
            req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

            var res = await Http.SendAsync(req);
            var responseBody = await res.Content.ReadAsStringAsync();

            activity?.SetTag("la.status_code", (int)res.StatusCode);
            activity?.SetTag("la.response_length", responseBody.Length);
            activity?.SetTag("la.result", res.IsSuccessStatusCode ? "success" : "http_error");

            var result = $"HTTP {(int)res.StatusCode} {res.StatusCode}\n";
            if (responseBody.Length > MaxResponseChars)
                result += responseBody[..MaxResponseChars] + $"\n... (truncated, {responseBody.Length} total chars)";
            else
                result += responseBody;

            if (!res.IsSuccessStatusCode)
                activity?.SetStatus(ActivityStatusCode.Error, $"HTTP {(int)res.StatusCode}");

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetTag("la.result", "exception");
            activity?.SetTag("la.error", ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return ex.ToString();
        }
    }

}
