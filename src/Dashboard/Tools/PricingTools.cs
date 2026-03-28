using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.Tools;

public static class PricingTools
{
    private static readonly HttpClient Http = new();
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "prices.azure.com"
    };

    public static IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(FetchUrl, "FetchUrl",
            "HTTP GET an allowed Azure API URL and returns raw JSON. Allowed hosts: prices.azure.com.");
    }

    private static async Task<string> FetchUrl(
        [Description("Full API URL to fetch, e.g. https://prices.azure.com/api/retail/prices?$top=10&$filter=serviceName eq 'Virtual Machines'")] string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !AllowedHosts.Contains(uri.Host) ||
            (uri.Scheme != "https" && uri.Scheme != "http"))
            return $"Error: URL must use an allowed host ({string.Join(", ", AllowedHosts)})";

        var json = await Http.GetStringAsync(url);
        var result = $"Current UTC time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\n{json}";
        return LargeResultHelper.Truncate(result, "FetchUrl");
    }
}
