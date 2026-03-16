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
            "Fetches data from an allowed Azure API URL and returns the raw response. " +
            "Allowed hosts: prices.azure.com. " +
            "You construct the full URL with any query parameters. The tool just fetches and returns the raw JSON. " +
            "Use the RunScript tool to process, filter, or transform large results with Python, bash, or SQL.");
    }

    private static async Task<string> FetchUrl(
        [Description("Full API URL to fetch, e.g. https://prices.azure.com/api/retail/prices?$top=10&$filter=serviceName eq 'Virtual Machines'")] string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !AllowedHosts.Contains(uri.Host) ||
            (uri.Scheme != "https" && uri.Scheme != "http"))
            return $"Error: URL must use an allowed host ({string.Join(", ", AllowedHosts)})";

        var json = await Http.GetStringAsync(url);
        return $"Current UTC time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\n{json}";
    }
}
