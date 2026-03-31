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
public class AzureQueryTools
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly ActivitySource Telemetry = new("AzureFinOps.AI");

    private const int MaxResponseChars = 80_000;

    private readonly UserTokens _tokens;

    public AzureQueryTools(UserTokens tokens) => _tokens = tokens;

    public IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(QueryAzure, "QueryAzure", @"Calls any Azure ARM REST API using the signed-in user's token and returns raw JSON.
Base: https://management.azure.com — provide path starting with /.
COST MGMT (Microsoft.CostManagement): POST /{scope}/.../query — cost analysis; /.../forecast; /.../generateCostDetailsReport — line-item cost data (replaces Consumption usageDetails); /.../generateReservationDetailsReport — reservation utilization line-item (replaces Consumption reservationDetails); GET /.../alerts; /.../dimensions; /.../benefitUtilizationSummaries; /.../benefitRecommendations.
BUDGETS (Microsoft.Consumption): GET /{scope}/.../budgets — list budgets and spend-vs-budget status; PUT to create/update budgets with thresholds and alert rules.
COST EXPORTS (Microsoft.CostManagement): GET /{scope}/.../exports — list scheduled cost data exports to storage; PUT to create/update.
SCHEDULED ACTIONS (Microsoft.CostManagement): GET /{scope}/.../scheduledActions — scheduled cost alert emails and reports.
COST VIEWS (Microsoft.CostManagement): GET /{scope}/.../views — pre-saved cost analysis views.
BILLING (Microsoft.Billing): GET .../billingAccounts; .../billingProfiles; .../invoiceSections; .../invoices; .../transactions; .../billingSubscriptions; .../departments; .../enrollmentAccounts; .../customers.
CONSUMPTION (Microsoft.Consumption): GET /{scope}/.../pricesheets; .../reservationSummaries; .../reservationRecommendations; .../reservationTransactions; .../lots; .../credits; .../balances; .../charges. NOTE: usageDetails and marketplaces are deprecated — prefer generateCostDetailsReport (Cost Details API 2025-03-01) or Exports for line-item cost data. reservationDetails is deprecated — prefer generateReservationDetailsReport (Microsoft.CostManagement).
RESERVATIONS (Microsoft.Capacity): GET .../reservationOrders; .../reservations; .../catalog; POST .../calculatePrice.
SAVINGS PLANS (Microsoft.BillingBenefits): GET .../savingsPlanOrders; .../savingsPlans.
ADVISOR (Microsoft.Advisor): GET /subscriptions/{id}/.../recommendations?$filter=Category eq 'Cost'.
RESOURCE GRAPH (Microsoft.ResourceGraph): POST .../resources — KQL across subs (body: {query,subscriptions}).
MONITOR (Microsoft.Insights): GET /{resourceId}/.../metrics; .../metricDefinitions; .../metricBaselines; .../diagnosticSettings.
ACTIVITY LOG (Microsoft.Insights): GET /{scope}/.../eventtypes/management/values?$filter=eventTimestamp ge '...' — who created/deleted/modified resources (cost attribution audit trail).
COMPUTE: GET /subscriptions/{id}/.../virtualMachines — list VMs; .../virtualMachineScaleSets — VMSS instances for right-sizing; .../skus — VM sizes per region.
AKS (Microsoft.ContainerService): GET /subscriptions/{id}/.../managedClusters — AKS clusters and node pool sizing for cost optimization.
NETWORK (Microsoft.Network): GET /subscriptions/{id}/.../virtualNetworks; .../publicIPAddresses; .../loadBalancers; .../applicationGateways; .../expressRouteCircuits; .../vpnGateways; .../natGateways — high-cost network resources.
STORAGE (Microsoft.Storage): GET /subscriptions/{id}/.../storageAccounts — storage tier optimization, lifecycle policies, access tier analysis.
SQL (Microsoft.Sql): GET /subscriptions/{id}/.../servers — SQL servers; .../servers/{name}/databases — DTU/vCore right-sizing.
APP SERVICE (Microsoft.Web): GET /subscriptions/{id}/.../serverfarms — App Service plans for right-sizing; .../sites — web apps.
AZURE ML (Microsoft.MachineLearningServices): GET /subscriptions/{id}/.../workspaces — ML workspaces; .../workspaces/{name}/computes — compute instances, clusters, GPU VMs for cost optimization and right-sizing; .../workspaces/{name}/onlineEndpoints — managed endpoints; .../workspaces/{name}/batchEndpoints.
DATABRICKS (Microsoft.Databricks): GET /subscriptions/{id}/.../workspaces — Databricks workspaces, pricing tier (standard/premium), managed RG for cost analysis; compare Databricks compute vs Azure ML compute vs standalone VM clusters.
SQL MI (Microsoft.Sql): GET /subscriptions/{id}/.../managedInstances — SQL Managed Instances for vCore right-sizing and cost optimization.
COSMOS DB (Microsoft.DocumentDB): GET /subscriptions/{id}/.../databaseAccounts — Cosmos DB accounts; .../databaseAccounts/{name}/sqlDatabases — databases with throughput (RU/s) for cost optimization, autoscale vs manual analysis.
REDIS (Microsoft.Cache): GET /subscriptions/{id}/.../redis — Redis Cache instances, tier (Basic/Standard/Premium) and capacity right-sizing.
DATA FACTORY (Microsoft.DataFactory): GET /subscriptions/{id}/.../factories — ADF instances; .../factories/{name}/pipelines — pipeline inventory for cost attribution.
SYNAPSE (Microsoft.Synapse): GET /subscriptions/{id}/.../workspaces — Synapse workspaces; .../workspaces/{name}/sqlPools — dedicated SQL pools (DWU right-sizing); .../workspaces/{name}/bigDataPools — Spark pools.
CONTAINER APPS (Microsoft.App): GET /subscriptions/{id}/.../containerApps — Container Apps; .../managedEnvironments — environments for cost analysis.
RESOURCE HEALTH (Microsoft.ResourceHealth): GET /{resourceId}/.../availabilityStatuses.
SECURITY (Microsoft.Security): GET /subscriptions/{id}/.../assessments — Defender for Cloud security assessments (identify exposed idle resources); .../secureScores — security posture.
SUBSCRIPTIONS: GET /subscriptions.
QUOTA (Microsoft.Quota): GET /{scope}/.../quotas.
CARBON (Microsoft.Carbon): POST .../carbonEmissionReports (preview).
POLICY (Microsoft.Authorization): GET /{scope}/.../policyDefinitions; .../policyAssignments; (Microsoft.PolicyInsights) .../policyStates/latest/summarize.
RBAC (Microsoft.Authorization): GET /{scope}/.../roleAssignments — role assignments for cost governance audit; .../roleDefinitions.
LOCKS (Microsoft.Authorization): GET /{scope}/.../locks — resource locks to prevent accidental deletion of critical resources.
MGMT GROUPS (Microsoft.Management): GET .../managementGroups; .../descendants; POST .../getEntities.
TAGS (Microsoft.Resources): GET /subscriptions/{id}/tagNames.
MIGRATE (Microsoft.Migrate): GET .../migrateProjects; .../assessments; .../assessedMachines.
SUPPORT (Microsoft.Support): GET .../supportTickets; .../services.
Scope = /subscriptions/{subId} or /subscriptions/{subId}/resourceGroups/{rg}.
For retail pricing use the FetchPricing tool with https://prices.azure.com (no auth required) instead of the deprecated Microsoft.Commerce/RateCard API.
Note: Only GET and POST methods are supported.");
    }

    private async Task<string> QueryAzure(
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

        var token = _tokens.AzureToken;
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
        result += responseBody;

        if (!res.IsSuccessStatusCode)
            activity?.SetStatus(ActivityStatusCode.Error, $"HTTP {(int)res.StatusCode}");

        return result;
    }
}
