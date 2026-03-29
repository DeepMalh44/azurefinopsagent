using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.Tools;

/// <summary>
/// Executes Python code in Azure Container Apps dynamic sessions (Hyper-V sandboxed).
/// Each user gets an isolated session identified by their user ID.
/// </summary>
public class CodeExecutionTools
{
    private const int MaxOutputChars = 50_000;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(240) };
    private static readonly TokenCredential Credential = new DefaultAzureCredential();
    private static readonly string[] TokenScopes = ["https://dynamicsessions.io/.default"];

    private readonly UserTokens _tokens;
    private readonly string _sessionPoolEndpoint;
    private readonly string _sessionId;

    public CodeExecutionTools(UserTokens tokens, string sessionPoolEndpoint, long userId)
    {
        _tokens = tokens;
        _sessionPoolEndpoint = sessionPoolEndpoint.TrimEnd('/');
        _sessionId = $"user-{userId}";
    }

    public IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(RunScript, "RunScript", @"Executes Python code in a secure, sandboxed environment (Hyper-V isolated). 220s timeout, 50KB output limit.
Pre-installed packages: requests, pandas, numpy, openpyxl, tabulate, scikit-learn, matplotlib, seaborn, scipy, beautifulsoup4, lxml, Pillow, sympy.
Use for data processing, calculations, API calls, and complex workflows.
Azure/Graph/LA tokens are available as variables: AZURE_TOKEN, GRAPH_TOKEN, LOG_ANALYTICS_TOKEN (pre-injected).
For Azure ARM APIs use https://management.azure.com with AZURE_TOKEN. For Graph use https://graph.microsoft.com with GRAPH_TOKEN.
For Log Analytics KQL use https://api.loganalytics.io/v1/workspaces/{wsId}/query with LOG_ANALYTICS_TOKEN.
For App Insights KQL use https://api.applicationinsights.io/v1/apps/{appId}/query with LOG_ANALYTICS_TOKEN.
Public (no auth): https://prices.azure.com/api/retail/prices?$filter=... — retail pricing.");
    }

    private async Task<string> RunScript(
        [Description("The Python code to execute.")] string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "Error: No code provided.";

        // Prepend token variables so scripts can use them
        var preamble = new StringBuilder();
        preamble.AppendLine($"AZURE_TOKEN = {PyStr(_tokens.AzureToken)}");
        preamble.AppendLine($"GRAPH_TOKEN = {PyStr(_tokens.GraphToken)}");
        preamble.AppendLine($"LOG_ANALYTICS_TOKEN = {PyStr(_tokens.LogAnalyticsToken)}");
        var fullCode = preamble + code;

        // Get Entra ID token for the session pool
        var tokenResult = await Credential.GetTokenAsync(
            new TokenRequestContext(TokenScopes), CancellationToken.None);

        var url = $"{_sessionPoolEndpoint}/executions?api-version=2025-10-02-preview&identifier={Uri.EscapeDataString(_sessionId)}";

        var payload = JsonSerializer.Serialize(new
        {
            properties = new
            {
                codeInputType = "inline",
                executionType = "synchronous",
                code = fullCode
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Token);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request);
        }
        catch (TaskCanceledException)
        {
            return "Error: Script timed out (exceeded 220s sandbox limit).";
        }

        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return $"Error: Session API returned {(int)response.StatusCode}. {Truncate(body)}";

        // Parse the response — extract stdout, stderr, result
        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(body);
            var props = json.GetProperty("properties");

            var stdout = props.TryGetProperty("stdout", out var so) ? so.GetString() : null;
            var stderr = props.TryGetProperty("stderr", out var se) ? se.GetString() : null;
            var result = props.TryGetProperty("result", out var re) ? re.ToString() : null;
            var status = props.TryGetProperty("status", out var st) ? st.GetString() : null;

            var output = new StringBuilder();
            if (!string.IsNullOrEmpty(stdout))
                output.AppendLine($"=== STDOUT ===\n{Truncate(stdout)}");
            if (!string.IsNullOrEmpty(stderr))
                output.AppendLine($"=== STDERR ===\n{Truncate(stderr)}");
            if (!string.IsNullOrEmpty(result) && result != "null")
                output.AppendLine($"=== RESULT ===\n{Truncate(result)}");
            if (output.Length == 0)
                output.AppendLine("(no output)");

            output.AppendLine($"\nStatus: {status ?? "unknown"}");
            return output.ToString();
        }
        catch
        {
            return $"=== RAW RESPONSE ===\n{Truncate(body)}";
        }
    }

    private static string PyStr(string? value)
        => value is null ? "None" : "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    private static string Truncate(string text)
        => text.Length > MaxOutputChars
            ? text[..MaxOutputChars] + $"\n... (truncated, {text.Length} total chars)"
            : text;
}
