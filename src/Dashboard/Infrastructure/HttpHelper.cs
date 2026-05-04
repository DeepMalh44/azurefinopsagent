using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;

namespace AzureFinOps.Dashboard.Infrastructure;

/// <summary>
/// Shared HTTP helper for all API tools — handles retry on 429, response formatting, and telemetry.
/// Eliminates duplicated retry loops, response formatting, and HttpClient instances across tools.
/// </summary>
public static class HttpHelper
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    public static readonly ActivitySource Telemetry = new("AzureFinOps.AI");

    /// <summary>
    /// Sends an HTTP request with automatic retry on 429 (up to 3 attempts).
    /// Returns formatted "HTTP {status}\n{body}" string for the LLM.
    /// </summary>
    public static async Task<string> SendWithRetryAsync(
        string url,
        string token,
        Activity? activity,
        string telemetryPrefix,
        HttpMethod? method = null,
        string? jsonBody = null,
        bool includeTimestamp = false,
        Dictionary<string, string>? extraHeaders = null,
        int? maxResponseChars = null)
    {
        method ??= HttpMethod.Get;

        HttpResponseMessage res = null!;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("User-Agent", "FinOps-Dashboard/1.0");

            if (extraHeaders is not null)
                foreach (var (key, value) in extraHeaders)
                    req.Headers.TryAddWithoutValidation(key, value);

            if (jsonBody is not null)
                req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            res = await Http.SendAsync(req);

            if ((int)res.StatusCode != 429) break;

            var retryAfter = res.Headers.RetryAfter?.Delta?.TotalSeconds
                          ?? res.Headers.RetryAfter?.Date?.Subtract(DateTimeOffset.UtcNow).TotalSeconds
                          ?? (attempt + 1) * 5;
            activity?.SetTag($"{telemetryPrefix}.retry_{attempt}", $"429, waiting {retryAfter:F0}s");
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(Math.Max(retryAfter, 1), 30)));
        }

        var responseBody = await res.Content.ReadAsStringAsync();

        activity?.SetTag($"{telemetryPrefix}.status_code", (int)res.StatusCode);
        activity?.SetTag($"{telemetryPrefix}.response_length", responseBody.Length);
        activity?.SetTag($"{telemetryPrefix}.result", res.IsSuccessStatusCode ? "success" : "http_error");

        if (!res.IsSuccessStatusCode)
            activity?.SetStatus(ActivityStatusCode.Error, $"HTTP {(int)res.StatusCode}");

        var result = $"HTTP {(int)res.StatusCode} {res.StatusCode}\n";
        if (includeTimestamp)
            result += $"Current UTC time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\n";

        // Trim chatty PUT/PATCH echoes — ARM returns the full resource (often 5–20KB) on success.
        // For bulk mutations this dominates LLM input tokens with no informational value.
        // Compact to a one-line {ok,status,name,id} summary; failures still return the full body
        // so the LLM can diagnose. Reads (GET) and query POSTs are untouched.
        if (res.IsSuccessStatusCode
            && (method == HttpMethod.Put || method == HttpMethod.Patch)
            && responseBody.Length > 256)
        {
            string? id = null, name = null;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(responseBody);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("id", out var idEl)) id = idEl.GetString();
                    if (doc.RootElement.TryGetProperty("name", out var nameEl)) name = nameEl.GetString();
                }
            }
            catch { /* not JSON or unexpected shape — fall through to full body */ }

            if (id is not null || name is not null)
            {
                result += $"{{\"ok\":true,\"status\":{(int)res.StatusCode},\"method\":\"{method.Method}\",\"name\":\"{name}\",\"id\":\"{id}\"}}";
                activity?.SetTag($"{telemetryPrefix}.response_trimmed", true);
                return result;
            }
        }

        if (maxResponseChars.HasValue && responseBody.Length > maxResponseChars.Value)
        {
            result += responseBody[..maxResponseChars.Value];
            result += $"\n\n[TRUNCATED — showing first {maxResponseChars.Value / 1024}KB of {responseBody.Length / 1024}KB. Use Python with pandas for full analysis.]";
        }
        else
        {
            result += responseBody;
        }

        return result;
    }

    /// <summary>
    /// Returns a standardized 401 error message when a token is missing.
    /// </summary>
    public static string TokenMissing(string tokenName, Activity? activity, string telemetryPrefix)
    {
        activity?.SetTag($"{telemetryPrefix}.result", "not_connected");
        activity?.SetStatus(ActivityStatusCode.Error, $"{tokenName} not connected");
        return $"HTTP 401 Unauthorized\n{tokenName} is null — the user must click 'Connect Azure' in the sidebar to authenticate, then retry.";
    }

    /// <summary>
    /// Centralised method-policy for all pass-through HTTP tools (Azure ARM, Microsoft Graph, etc.).
    /// Allows GET/POST/PUT/PATCH. Blocks DELETE at the code level — the user's RBAC role is the
    /// effective access boundary for everything else.
    /// Returns the parsed <see cref="HttpMethod"/>, or a ready-to-return error string when the
    /// method is rejected. Callers do: <c>var (m, err) = HttpHelper.ResolveMethod(...); if (err != null) return err;</c>
    /// </summary>
    public static (HttpMethod? Method, string? ErrorResponse) ResolveMethod(
        string? method,
        Activity? activity,
        string telemetryPrefix)
    {
        var normalized = (method ?? "GET").Trim().ToUpperInvariant();

        if (normalized == "DELETE")
        {
            activity?.SetTag($"{telemetryPrefix}.result", "blocked_delete");
            activity?.SetStatus(ActivityStatusCode.Error, "DELETE blocked");
            return (null, "HTTP 403 Forbidden\nThis agent does not perform DELETE operations. Generate a script via GenerateScript for the user to review and run themselves.");
        }

        var resolved = normalized switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "PATCH" => HttpMethod.Patch,
            _ => null
        };

        if (resolved is null)
        {
            activity?.SetTag($"{telemetryPrefix}.result", "invalid_method");
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid method");
            return (null, $"HTTP 400 BadRequest\nInvalid method: '{method}'. Allowed: GET, POST, PUT, PATCH.");
        }

        return (resolved, null);
    }
}
