using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.Tools;

/// <summary>
/// Queries Microsoft Graph API using the user's delegated Graph token.
/// Used for license inventory, directory objects, and org structure for FinOps chargebacks.
/// </summary>
public class GraphQueryTools
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly ActivitySource Telemetry = new("AzureFinOps.AI");
    private const int MaxResponseChars = 80_000;

    private readonly UserTokens _tokens;

    public GraphQueryTools(UserTokens tokens) => _tokens = tokens;

    public IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(QueryGraph, "QueryGraph", @"Calls Microsoft Graph API (https://graph.microsoft.com) using the signed-in user's token.
Provide path starting with /. Returns raw JSON.
DATA SCOPING: ALWAYS use $select to pick only needed fields, $top to limit rows, $filter to scope. Never fetch full user objects — use $select=displayName,department,userPrincipalName. For large tenants, use $top=50 and paginate via @odata.nextLink.
LICENSES: GET /v1.0/subscribedSkus — license inventory; GET /v1.0/users?$select=displayName,assignedLicenses&$top=50 — license assignments; GET /v1.0/reports/getOffice365ActiveUserDetail(period='D30') — active usage.
M365 USAGE REPORTS: GET /v1.0/reports/getMailboxUsageDetail(period='D30') — Exchange mailbox usage (find unused mailboxes = license waste); GET /v1.0/reports/getTeamsUserActivityUserDetail(period='D30') — Teams activity vs assigned licenses; GET /v1.0/reports/getOneDriveUsageAccountDetail(period='D30') — OneDrive storage usage per user; GET /v1.0/reports/getSharePointSiteUsageDetail(period='D30') — SharePoint site storage and activity; GET /v1.0/reports/getOffice365ActiveUserCounts(period='D30') — overall M365 active user trends.
DEVICE MGMT: GET /beta/deviceManagement/managedDevices — Intune managed devices for license reconciliation; GET /beta/deviceManagement/deviceCompliancePolicySettingStateSummaries — compliance posture.
DIRECTORY: GET /v1.0/organization — tenant info; GET /v1.0/groups — groups for chargeback mapping; GET /v1.0/users?$select=displayName,department,companyName — org structure for cost allocation.
M365 COPILOT USAGE: GET /v1.0/reports/getM365AppUserDetail(period='D30') — per-user M365 app usage for license optimization; GET /beta/copilot/reports/microsoft365CopilotUsageUserDetail — Copilot seat usage vs assigned (identify unused Copilot licenses).
APPS: GET /v1.0/applications — app registrations; GET /v1.0/servicePrincipals — service principals.");
    }

    private async Task<string> QueryGraph(
        [Description("API path starting with /, e.g. /v1.0/subscribedSkus")] string path)
    {
        using var activity = Telemetry.StartActivity("QueryGraph");
        activity?.SetTag("graph.path", path);

        var token = _tokens.GraphToken;
        if (string.IsNullOrEmpty(token))
        {
            activity?.SetTag("graph.result", "not_connected");
            activity?.SetStatus(ActivityStatusCode.Error, "Graph not connected");
            return "HTTP 401 Unauthorized\nTokenContext.GraphToken is null — no Microsoft Graph token available. The user must click 'Connect Azure' in the sidebar to authenticate, then retry.";
        }

        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/'))
        {
            activity?.SetTag("graph.result", "invalid_path");
            return $"HTTP 400 BadRequest\nInvalid path: '{path}'. Path must start with /.";
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://graph.microsoft.com{path}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("User-Agent", "FinOps-Dashboard/1.0");

        var res = await Http.SendAsync(req);
        var responseBody = await res.Content.ReadAsStringAsync();

        activity?.SetTag("graph.status_code", (int)res.StatusCode);
        activity?.SetTag("graph.response_length", responseBody.Length);
        activity?.SetTag("graph.result", res.IsSuccessStatusCode ? "success" : "http_error");

        var result = $"HTTP {(int)res.StatusCode} {res.StatusCode}\n";
        result += responseBody;

        if (!res.IsSuccessStatusCode)
            activity?.SetStatus(ActivityStatusCode.Error, $"HTTP {(int)res.StatusCode}");

        return result;
    }
}
