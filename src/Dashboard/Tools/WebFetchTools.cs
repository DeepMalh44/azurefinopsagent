using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.Tools;

/// <summary>
/// Web fetch tool: retrieve content from any public URL and convert to readable text.
/// Equivalent to Copilot CLI's web_fetch tool.
/// </summary>
public static class WebFetchTools
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
    private const int MaxResponseChars = 100_000;

    static WebFetchTools()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("FinOps-Agent/1.0");
        Http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        Http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        Http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
    }

    public static IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(FetchWebPage, "FetchWebPage",
            "Fetch content from a public URL. Returns clean text extracted from HTML, or raw JSON/text. Use for documentation, blog posts, release notes, or any public web content.");
    }

    private static async Task<string> FetchWebPage(
        [Description("Full URL to fetch (must be https)")] string url,
        [Description("Optional: extract only content matching this query/topic")] string? query)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "Error: Invalid URL.";
        if (uri.Scheme != "https")
            return "Error: Only HTTPS URLs are allowed.";
        // Block private/internal IPs
        if (uri.Host == "localhost" || uri.Host.StartsWith("127.") || uri.Host.StartsWith("10.") ||
            uri.Host.StartsWith("192.168.") || uri.Host.StartsWith("172."))
            return "Error: Internal/private URLs are not allowed.";

        var response = await Http.GetAsync(url);
        if (!response.IsSuccessStatusCode)
            return $"Error: HTTP {(int)response.StatusCode} {response.ReasonPhrase}";

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        var body = await response.Content.ReadAsStringAsync();

        // Truncate
        if (body.Length > MaxResponseChars)
            body = body[..MaxResponseChars] + $"\n\n[Truncated: {body.Length:N0} total chars]";

        // If HTML, strip tags to extract text
        if (contentType.Contains("html"))
        {
            body = StripHtml(body);
        }

        var result = $"URL: {url}\nContent-Type: {contentType}\nLength: {body.Length:N0} chars\n\n{body}";
        return LargeResultHelper.Truncate(result, "FetchWebPage");
    }

    private static string StripHtml(string html)
    {
        // Remove script and style blocks
        html = Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        // Remove HTML tags
        html = Regex.Replace(html, @"<[^>]+>", " ");
        // Decode common entities
        html = html.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"");
        // Collapse whitespace
        html = Regex.Replace(html, @"\s+", " ").Trim();
        return html;
    }
}
