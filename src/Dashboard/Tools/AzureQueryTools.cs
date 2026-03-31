using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.Tools;

/// <summary>
/// Single tool for querying any Azure ARM API using the user's delegated access token.
/// The LLM constructs the URL and optional body; this tool executes the HTTP request.
/// All calls are traced via OpenTelemetry → Application Insights for analysis.
/// </summary>
public class AzureQueryTools
{
    private readonly UserTokens _tokens;

    // Allowlist of read-only POST path suffixes — blocks mutating ARM actions
    // (deallocate, start, restart, powerOff, delete, write, etc.) at the code level.
    // Only query/report/calculation POST endpoints are permitted.
    private static readonly string[] SafePostSuffixes =
    {
        "/query",                              // Cost Management cost/forecast queries
        "/forecast",                           // Cost Management forecast
        "/generatecostdetailsreport",          // Cost Details report (replaces usageDetails)
        "/generatereservationdetailsreport",   // Reservation utilization line-item report
        "/resources",                          // Resource Graph KQL queries
        "/calculateprice",                     // Reservation/Savings Plan price simulation
        "/calculateexchange",                  // Reservation exchange simulation
        "/validatepurchase",                   // Savings Plan purchase validation
        "/carbonemissionreports",              // Carbon emission reports
        "/getentities",                        // Management Group entity listing
        "/summarize",                          // Policy Insights summarization
    };

    private static bool IsReadOnlyPost(string path)
    {
        var pathOnly = path.Split('?')[0].TrimEnd('/').ToLowerInvariant();
        return SafePostSuffixes.Any(suffix => pathOnly.EndsWith(suffix));
    }

    public AzureQueryTools(UserTokens tokens) => _tokens = tokens;

    public IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(QueryAzure, "QueryAzure", @"READ-ONLY: Queries Azure ARM REST APIs using the signed-in user's token and returns raw JSON. This tool CANNOT create, update, or delete any Azure resources — all write operations are blocked at the code level.
Base: https://management.azure.com — provide path starting with /.
Allowed methods: GET (any path) and POST (restricted to read-only query/report endpoints only — e.g. /query, /forecast, /resources, /generateCostDetailsReport, /calculatePrice, /carbonEmissionReports). POST to mutating endpoints (deallocate, start, restart, delete, etc.) will be rejected.
DATA SCOPING: For Cost Management POST .../query, ALWAYS use grouping (ServiceName, ResourceGroup, MeterCategory) and date granularity (Daily/Monthly). Never request raw ungrouped cost data. For Resource Graph POST .../resources, use KQL 'project' and 'top 20' — never select all columns. For list APIs (VMs, storage, etc.), results are already scoped by subscription.
COST MGMT (Microsoft.CostManagement): POST /{scope}/.../query — cost analysis; /.../forecast; /.../generateCostDetailsReport — line-item cost data (replaces Consumption usageDetails); /.../generateReservationDetailsReport — reservation utilization line-item; GET /.../alerts; /.../dimensions; /.../benefitUtilizationSummaries; /.../benefitRecommendations; /.../costAllocationRules — split shared costs across scopes.
BUDGETS (Microsoft.Consumption): GET /{scope}/.../budgets — list budgets and spend-vs-budget status.
COST EXPORTS (Microsoft.CostManagement): GET /{scope}/.../exports — list scheduled cost data exports to storage.
SCHEDULED ACTIONS (Microsoft.CostManagement): GET /{scope}/.../scheduledActions — scheduled cost alert emails and reports.
COST VIEWS (Microsoft.CostManagement): GET /{scope}/.../views — pre-saved cost analysis views.
BILLING (Microsoft.Billing): GET .../billingAccounts; .../billingProfiles; .../invoiceSections; .../invoices; .../transactions; .../billingSubscriptions; .../departments; .../enrollmentAccounts; .../customers.
CONSUMPTION (Microsoft.Consumption): GET /{scope}/.../pricesheets; .../reservationSummaries; .../reservationRecommendations; .../reservationTransactions; .../lots; .../credits; .../balances; .../charges; .../marketplaces — third-party Marketplace charges. NOTE: usageDetails is deprecated — prefer generateCostDetailsReport (Cost Details API 2025-03-01) or Exports. reservationDetails is deprecated — prefer generateReservationDetailsReport.
RESERVATIONS (Microsoft.Capacity): GET .../reservationOrders; .../reservations; .../catalog; POST .../calculatePrice; .../calculateExchange — simulate exchange/return costs (read-only calculations).
SAVINGS PLANS (Microsoft.BillingBenefits): GET .../savingsPlanOrders; .../savingsPlans; POST .../calculatePrice; .../validatePurchase.
ADVISOR (Microsoft.Advisor): GET /subscriptions/{id}/.../recommendations?$filter=Category eq 'Cost'.
RESOURCE GRAPH (Microsoft.ResourceGraph): POST .../resources — KQL across subs (body: {query,subscriptions}).
MONITOR (Microsoft.Insights): GET /{resourceId}/.../metrics; .../metricDefinitions; .../metricBaselines; .../diagnosticSettings; .../autoscaleSettings.
ACTIVITY LOG (Microsoft.Insights): GET /{scope}/.../eventtypes/management/values?$filter=eventTimestamp ge '...' — who created/deleted/modified resources (cost attribution audit trail).
COMPUTE: GET /subscriptions/{id}/.../virtualMachines — list VMs; .../virtualMachineScaleSets — VMSS instances for right-sizing; .../skus — VM sizes per region; .../disks — managed disks (find unattached/orphaned disks).
AKS (Microsoft.ContainerService): GET /subscriptions/{id}/.../managedClusters — AKS clusters and node pool sizing for cost optimization.
NETWORK (Microsoft.Network): GET /subscriptions/{id}/.../virtualNetworks; .../publicIPAddresses; .../loadBalancers; .../applicationGateways; .../expressRouteCircuits; .../vpnGateways; .../natGateways; .../azureFirewalls; .../privateEndpoints — high-cost network resources.
STORAGE (Microsoft.Storage): GET /subscriptions/{id}/.../storageAccounts — storage tier optimization, lifecycle policies, access tier analysis.
SQL (Microsoft.Sql): GET /subscriptions/{id}/.../servers — SQL servers; .../servers/{name}/databases — DTU/vCore right-sizing.
APP SERVICE (Microsoft.Web): GET /subscriptions/{id}/.../serverfarms — App Service plans for right-sizing; .../sites — web apps and function apps.
LOG ANALYTICS (Microsoft.OperationalInsights): GET /subscriptions/{id}/.../workspaces — Log Analytics workspaces; .../workspaces/{name}/usages — data ingestion volume per table; .../workspaces/{name}/tables — table retention and ingestion plans (Analytics/Basic/Archive).
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
For retail pricing use the built-in fetch tool with https://prices.azure.com (no auth required). ALWAYS filter by armRegionName + serviceName + armSkuName and use $top=20 for comparisons.
SECURITY: Only GET and read-only POST endpoints are allowed. PUT, PATCH, DELETE, and mutating POST actions are blocked at the HTTP client level.");
    }

    private async Task<string> QueryAzure(
        [Description("HTTP method: GET or POST")] string method,
        [Description("API path starting with /, e.g. /subscriptions?api-version=2022-12-01")] string path,
        [Description("Optional JSON request body for POST requests. Omit or leave empty for GET.")] string? body = null)
    {
        using var activity = HttpHelper.Telemetry.StartActivity("QueryAzure");
        activity?.SetTag("azure.method", method);
        activity?.SetTag("azure.path", path);
        activity?.SetTag("azure.has_body", !string.IsNullOrWhiteSpace(body));
        if (!string.IsNullOrWhiteSpace(body))
            activity?.SetTag("azure.body", body.Length > 2000 ? body[..2000] + "..." : body);

        var token = _tokens.AzureToken;
        if (string.IsNullOrEmpty(token))
            return HttpHelper.TokenMissing("AzureToken", activity, "azure");

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

        // Enforce read-only: POST requests must target known query/report endpoints
        if (httpMethod == HttpMethod.Post && !IsReadOnlyPost(path))
        {
            activity?.SetTag("azure.result", "blocked_mutating_post");
            activity?.SetStatus(ActivityStatusCode.Error, "Mutating POST blocked");
            return $"HTTP 403 Forbidden\nThis agent is read-only. POST is only allowed to query/report endpoints (e.g. /query, /forecast, /resources, /generateCostDetailsReport). The requested path '{path}' is not in the allowlist. Use GET to read data instead.";
        }

        return await HttpHelper.SendWithRetryAsync(
            $"https://management.azure.com{path}",
            token, activity, "azure",
            method: httpMethod,
            jsonBody: httpMethod == HttpMethod.Post && !string.IsNullOrWhiteSpace(body) ? body : null,
            includeTimestamp: true);
    }
}
