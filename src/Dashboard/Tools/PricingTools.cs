using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.Tools;

public static class PricingTools
{
    private static readonly HttpClient Http = new();
    private const int MaxResponseChars = 40_000;

    public static IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(GetAzureRetailPrices, "GetAzureRetailPrices",
            "Fetches Azure retail prices by calling the Azure Retail Prices REST API. " +
            "You must construct the full API URL yourself including query parameters. " +
            "Base URL: https://prices.azure.com/api/retail/prices " +
            "Supports OData $filter and $top query params. " +
            "Filter fields: serviceName, armRegionName, armSkuName, priceType, productName, skuName, meterName, serviceFamily, currencyCode. " +
            "Filter operators: eq, ne, and, or, contains(). " +
            "If the response is large, a schema preview + summary is returned. " +
            "Then use QueryAzurePrices to get the full data with only the fields you need. No auth required.");

        yield return AIFunctionFactory.Create(QueryAzurePrices, "QueryAzurePrices",
            "Fetches Azure retail prices and returns ONLY the fields you specify, keeping the response compact. " +
            "Use this after calling GetAzureRetailPrices to learn the schema. " +
            "Provide the same API URL and a comma-separated list of fields to extract. " +
            "Optionally sort by a field and limit the number of results. " +
            "Returns a compact JSON array with just your selected fields from ALL matching items.");
    }

    private static async Task<string> GetAzureRetailPrices(
        [Description("Full Azure Retail Prices API URL with query parameters, e.g. " +
                     "https://prices.azure.com/api/retail/prices?$top=10&$filter=serviceName eq 'Virtual Machines'")] string url)
    {
        var json = await Http.GetStringAsync(url);

        if (json.Length <= MaxResponseChars)
            return $"Current UTC time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\n{json}";

        // Large response: return schema preview + summary
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var totalItems = root.TryGetProperty("Items", out var items) ? items.GetArrayLength() : 0;
        var hasMore = root.TryGetProperty("NextPageLink", out var next) && next.ValueKind != JsonValueKind.Null;

        // First 3 items as schema example
        var preview = items.EnumerateArray().Take(3).Select(i => i.GetRawText()).ToList();

        // Collect unique values from ALL items
        var skus = new HashSet<string>();
        var regions = new HashSet<string>();
        var products = new HashSet<string>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("armSkuName", out var s) && s.ValueKind == JsonValueKind.String) skus.Add(s.GetString()!);
            if (item.TryGetProperty("armRegionName", out var r) && r.ValueKind == JsonValueKind.String) regions.Add(r.GetString()!);
            if (item.TryGetProperty("productName", out var p) && p.ValueKind == JsonValueKind.String) products.Add(p.GetString()!);
        }

        // List available fields from first item
        var fields = items.EnumerateArray().FirstOrDefault().EnumerateObject().Select(p => p.Name).ToList();

        return $"Current UTC time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\n" +
               $"⚠ LARGE RESPONSE: {totalItems} items{(hasMore ? " (more pages exist)" : "")}. Use QueryAzurePrices with the same URL to get compact data.\n" +
               $"Available fields: {string.Join(", ", fields)}\n" +
               $"Unique SKUs ({skus.Count}): {string.Join(", ", skus.Order())}\n" +
               $"Unique regions ({regions.Count}): {string.Join(", ", regions.Order())}\n" +
               $"Unique products ({products.Count}): {string.Join(", ", products.Order())}\n" +
               $"Schema preview (first 3 items):\n[{string.Join(",", preview)}]";
    }

    private static async Task<string> QueryAzurePrices(
        [Description("Full Azure Retail Prices API URL (same as GetAzureRetailPrices)")] string url,
        [Description("Comma-separated field names to extract, e.g. 'armSkuName,armRegionName,retailPrice,productName'")] string fields,
        [Description("Optional: field name to sort by, e.g. 'retailPrice'. Prefix with '-' for descending, e.g. '-retailPrice'")] string? sortBy,
        [Description("Optional: max number of items to return (default: all)")] string? limit)
    {
        var json = await Http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("Items", out var items))
            return "No Items found in response.";

        var fieldList = fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Project each item to only the requested fields
        var projected = new List<Dictionary<string, object?>>();
        foreach (var item in items.EnumerateArray())
        {
            var row = new Dictionary<string, object?>();
            foreach (var f in fieldList)
            {
                if (item.TryGetProperty(f, out var val))
                    row[f] = val.ValueKind switch
                    {
                        JsonValueKind.String => val.GetString(),
                        JsonValueKind.Number => val.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => val.GetRawText()
                    };
            }
            projected.Add(row);
        }

        // Sort
        if (!string.IsNullOrWhiteSpace(sortBy))
        {
            var desc = sortBy.StartsWith('-');
            var sortField = desc ? sortBy[1..] : sortBy;
            projected = (desc
                ? projected.OrderByDescending(r => r.TryGetValue(sortField, out var v) ? v : null, Comparer<object?>.Default)
                : projected.OrderBy(r => r.TryGetValue(sortField, out var v) ? v : null, Comparer<object?>.Default))
                .ToList();
        }

        // Limit
        if (!string.IsNullOrWhiteSpace(limit) && int.TryParse(limit, out var max))
            projected = projected.Take(max).ToList();

        var result = JsonSerializer.Serialize(projected);
        return $"Current UTC time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\nItems: {projected.Count}\n{result}";
    }
}
