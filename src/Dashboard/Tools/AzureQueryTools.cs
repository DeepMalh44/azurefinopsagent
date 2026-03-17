using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.Tools;

/// <summary>
/// Single tool for querying any Azure ARM API using the user's delegated access token.
/// The LLM constructs the URL and optional body; this tool executes the HTTP request.
/// All calls are traced via OpenTelemetry → Application Insights for analysis.
/// </summary>
public static class AzureQueryTools
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly ActivitySource Telemetry = new("AzureFinOps.AI");

    private const int MaxResponseChars = 80_000;

    public static IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(QueryAzure, "QueryAzure", @"Calls any Azure ARM REST API using the signed-in user's token and returns raw JSON.
Base: https://management.azure.com — provide path starting with /.
COST MGMT (Microsoft.CostManagement): POST /{scope}/.../query — cost analysis; /.../forecast; /.../generateCostDetailsReport — line-item; GET /.../alerts; /.../dimensions; /.../benefitUtilizationSummaries; /.../benefitRecommendations.
BILLING (Microsoft.Billing): GET .../billingAccounts; .../billingProfiles; .../invoiceSections; .../invoices; .../transactions; .../billingSubscriptions; .../departments; .../enrollmentAccounts; .../customers.
CONSUMPTION (Microsoft.Consumption): GET /{scope}/.../usageDetails; .../marketplaces; .../pricesheets; .../reservationSummaries; .../reservationDetails; .../reservationRecommendations; .../reservationTransactions; .../lots; .../credits; .../balances; .../charges.
RESERVATIONS (Microsoft.Capacity): GET .../reservationOrders; .../reservations; .../catalog; POST .../calculatePrice.
SAVINGS PLANS (Microsoft.BillingBenefits): GET .../savingsPlanOrders; .../savingsPlans.
ADVISOR (Microsoft.Advisor): GET /subscriptions/{id}/.../recommendations?$filter=Category eq 'Cost'.
RESOURCE GRAPH (Microsoft.ResourceGraph): POST .../resources — KQL across subs (body: {query,subscriptions}).
MONITOR (Microsoft.Insights): GET /{resourceId}/.../metrics; .../metricDefinitions; .../metricBaselines; .../diagnosticSettings.
COMPUTE: GET /subscriptions/{id}/.../skus — VM sizes per region.
RESOURCE HEALTH (Microsoft.ResourceHealth): GET /{resourceId}/.../availabilityStatuses.
SUBSCRIPTIONS: GET /subscriptions.
QUOTA (Microsoft.Quota): GET /{scope}/.../quotas.
CARBON (Microsoft.Carbon): POST .../carbonEmissionReports (preview).
POLICY (Microsoft.Authorization): GET /{scope}/.../policyDefinitions; .../policyAssignments; (Microsoft.PolicyInsights) .../policyStates/latest/summarize.
MGMT GROUPS (Microsoft.Management): GET .../managementGroups; .../descendants; POST .../getEntities.
TAGS (Microsoft.Resources): GET /subscriptions/{id}/tagNames.
MIGRATE (Microsoft.Migrate): GET .../migrateProjects; .../assessments; .../assessedMachines.
SUPPORT (Microsoft.Support): GET .../supportTickets; .../services.
Scope = /subscriptions/{subId} or /subscriptions/{subId}/resourceGroups/{rg}.
Note: Only GET and POST methods are supported.");
    }

    private static async Task<string> QueryAzure(
        [Description("HTTP method: GET or POST")] string method,
        [Description("API path starting with /, e.g. /subscriptions?api-version=2022-12-01")] string path,
        [Description("Optional JSON request body for POST requests. Omit or leave empty for GET.")] string? body = null)
    {
        using var activity = Telemetry.StartActivity("QueryAzure");
        activity?.SetTag("azure.method", method);
        activity?.SetTag("azure.path", path);
        activity?.SetTag("azure.has_body", !string.IsNullOrWhiteSpace(body));
        if (!string.IsNullOrWhiteSpace(body))
            activity?.SetTag("azure.body", body.Length > 2000 ? body[..2000] + "..." : body);

        var token = TokenContext.AzureToken;
        if (string.IsNullOrEmpty(token))
        {
            activity?.SetTag("azure.result", "not_connected");
            activity?.SetStatus(ActivityStatusCode.Error, "Azure not connected");
            return "HTTP 401 Unauthorized\nTokenContext.AzureToken is null — no Azure token available for this request. The user must click 'Connect Azure' in the sidebar to authenticate via Microsoft Entra ID, then retry.";
        }

        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/'))
        {
            activity?.SetTag("azure.result", "invalid_path");
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid path");
            return $"HTTP 400 BadRequest\nInvalid path: '{path}'. Path must start with /.";
        }

        var httpMethod = method?.Trim().ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            _ => null
        };

        if (httpMethod is null)
        {
            activity?.SetTag("azure.result", "invalid_method");
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid method");
            return $"HTTP 400 BadRequest\nInvalid method: '{method}'. Only GET and POST are supported.";
        }

        var url = $"https://management.azure.com{path}";

        try
        {
            using var req = new HttpRequestMessage(httpMethod, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("User-Agent", "FinOps-Dashboard/1.0");

            if (httpMethod == HttpMethod.Post && !string.IsNullOrWhiteSpace(body))
                req.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

            var res = await Http.SendAsync(req);
            var responseBody = await res.Content.ReadAsStringAsync();

            activity?.SetTag("azure.status_code", (int)res.StatusCode);
            activity?.SetTag("azure.response_length", responseBody.Length);
            activity?.SetTag("azure.result", res.IsSuccessStatusCode ? "success" : "http_error");

            var result = $"HTTP {(int)res.StatusCode} {res.StatusCode}\n";
            result += $"Current UTC time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\n";

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
            activity?.SetTag("azure.result", "exception");
            activity?.SetTag("azure.error", ex.Message);
            activity?.SetTag("azure.error_type", ex.GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return ex.ToString();
        }
    }
}
