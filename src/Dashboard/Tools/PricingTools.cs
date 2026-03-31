using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.Tools;

/// <summary>
/// Fetches Azure retail pricing from the public Prices API (no auth required).
/// Replaces the built-in fetch tool which truncates responses at ~8KB.
/// </summary>
public static class PricingTools
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly ActivitySource Telemetry = new("AzureFinOps.AI");

    public static IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(FetchPricing, "FetchPricing", @"Fetches Azure retail pricing from https://prices.azure.com/api/retail/prices — no auth required.
Returns full untruncated JSON. Use this instead of the built-in fetch tool for pricing queries.

IMPORTANT: Always scope queries tightly to avoid massive responses. An unfiltered query returns 4M+ items.

REQUIRED FILTERS — always include at least two of these to keep responses manageable:
  - armRegionName eq 'eastus' — ALWAYS filter by region (60+ regions = 60x data without this)
  - serviceName eq 'Virtual Machines' — filter by service
  - armSkuName eq 'Standard_D4s_v5' — specific VM SKU
  - priceType eq 'Consumption' — pay-as-you-go only (excludes Reservation, DevTestConsumption, Savings Plan prices unless needed)

USEFUL FILTERS:
  - productName eq 'Virtual Machines DSv5 Series' — narrower than serviceName
  - meterName eq 'D4s v5' or meterName eq 'D4s v5 Spot' — specific meter
  - type eq 'Consumption' or type eq 'Reservation' — pricing model
  - contains(skuName, 'Spot') — spot pricing
  - unitOfMeasure eq '1 Hour' — hourly compute pricing

PAGINATION: Responses return max 100 items per page. Use nextPageLink from the response for more.
Use $top=20 to limit results when you only need a sample or comparison.

MULTI-REGION COMPARISON: Make separate calls per region (2-3 regions), then compare — do NOT omit armRegionName.

CURRENCY: Add ?currencyCode='EUR' before $filter for non-USD pricing.

Examples (well-scoped):
  ?$filter=serviceName eq 'Virtual Machines' and armSkuName eq 'Standard_D4s_v5' and armRegionName eq 'eastus' and priceType eq 'Consumption'
  ?$filter=serviceName eq 'Storage' and productName eq 'Blob Storage' and armRegionName eq 'westeurope' and priceType eq 'Consumption'
  ?currencyCode='EUR'&$filter=serviceName eq 'Virtual Machines' and armRegionName eq 'northeurope' and armSkuName eq 'Standard_D4s_v5'
  ?$top=20&$filter=serviceName eq 'Azure Cosmos DB' and armRegionName eq 'eastus' and priceType eq 'Consumption'

BAD (too broad — will return thousands of items):
  ?$filter=serviceName eq 'Virtual Machines' — missing region, returns 60+ regions x all SKUs
  ?$filter=armRegionName eq 'eastus' — missing service, returns every service in that region");
    }

    private static async Task<string> FetchPricing(
        [Description("Full URL starting with https://prices.azure.com/api/retail/prices, including OData $filter query string")] string url)
    {
        using var activity = Telemetry.StartActivity("FetchPricing");
        activity?.SetTag("pricing.url", url?.Length > 500 ? url[..500] + "..." : url);

        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("https://prices.azure.com/", StringComparison.OrdinalIgnoreCase))
        {
            activity?.SetTag("pricing.result", "invalid_url");
            return "Error: URL must start with https://prices.azure.com/. Example: https://prices.azure.com/api/retail/prices?$filter=serviceName eq 'Virtual Machines' and armSkuName eq 'Standard_D4s_v5'";
        }

        var res = await Http.GetAsync(url);
        var body = await res.Content.ReadAsStringAsync();

        activity?.SetTag("pricing.status_code", (int)res.StatusCode);
        activity?.SetTag("pricing.response_length", body.Length);
        activity?.SetTag("pricing.result", res.IsSuccessStatusCode ? "success" : "http_error");

        // Cap at 200K chars to avoid overwhelming the LLM context
        if (body.Length > 200_000)
            body = body[..200_000] + "\n... [truncated at 200K chars — add more specific filters to narrow results]";

        var result = $"HTTP {(int)res.StatusCode} {res.StatusCode}\n";
        result += $"Response size: {body.Length:N0} chars\n";
        result += body;

        return result;
    }
}
