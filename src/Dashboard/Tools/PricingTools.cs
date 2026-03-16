using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.Tools;

public static class PricingTools
{
    private static readonly HttpClient Http = new();

    public static IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(GetAzureRetailPrices, "GetAzureRetailPrices",
            "Gets current retail prices and available services/resources from the Azure Retail Prices API. " +
            "Returns raw JSON with pricing details including unit price, meter name, SKU, region, and currency. " +
            "No authentication required. All parameters are optional — call with no filters to browse all Azure services, " +
            "or filter by region to see what's available in a specific region. " +
            "Use this for: Azure pricing, cost estimation, listing available services/resources, and comparing SKUs across regions.");
    }

    private static async Task<string> GetAzureRetailPrices(
        [Description("Optional Azure service name (e.g. 'Virtual Machines', 'Azure Cosmos DB', 'Storage', 'Azure App Service'). " +
                     "Leave empty to list all available services.")] string? serviceName,
        [Description("Optional Azure region name (e.g. 'eastus', 'westeurope'). Leave empty for all regions.")] string? armRegionName,
        [Description("Optional ARM SKU name to narrow results (e.g. 'Standard_D2s_v3'). Leave empty for all SKUs.")] string? armSkuName)
    {
        var filters = new List<string>();

        if (!string.IsNullOrWhiteSpace(serviceName))
            filters.Add($"serviceName eq '{serviceName}'");

        if (!string.IsNullOrWhiteSpace(armRegionName))
            filters.Add($"armRegionName eq '{armRegionName}'");

        if (!string.IsNullOrWhiteSpace(armSkuName))
            filters.Add($"armSkuName eq '{armSkuName}'");

        var url = "https://prices.azure.com/api/retail/prices";
        var filter = string.Join(" and ", filters);

        if (filters.Count > 0)
            url += $"?$filter={Uri.EscapeDataString(filter)}";

        var json = await Http.GetStringAsync(url);
        return $"Current UTC time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\nFilter: {(filter.Length > 0 ? filter : "(none — all services)")}\n{json}";
    }
}
