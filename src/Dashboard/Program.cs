using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.AI;
using AzureFinOps.Dashboard.Tools;

var builder = WebApplication.CreateBuilder(args);

// Load local settings only in Development
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// Data protection for cookie encryption
builder.Services.AddDataProtection();

// Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(24);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.AddHttpClient();

// HTTPS redirection (respects X-Forwarded-Proto via forwarded headers middleware)
if (!builder.Environment.IsDevelopment())
    builder.Services.AddHttpsRedirection(options => options.HttpsPort = 443);

// OpenTelemetry + Application Insights
var appInsightsCs = builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrEmpty(appInsightsCs))
{
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor(o => o.ConnectionString = appInsightsCs)
        .WithTracing(t => t.AddSource("AzureFinOps.AI"))
        .WithMetrics(m => m.AddMeter("AzureFinOps.AI"));
}

// ActivitySource for custom AI telemetry spans
var aiActivitySource = new ActivitySource("AzureFinOps.AI");
// Custom metrics
var aiMeter = new Meter("AzureFinOps.AI");
var chatRequestCounter = aiMeter.CreateCounter<long>("finops.chat.requests", description: "Total chat requests");
var chatErrorCounter = aiMeter.CreateCounter<long>("finops.chat.errors", description: "Chat request errors");
var toolCallCounter = aiMeter.CreateCounter<long>("finops.tool.calls", description: "Tool call invocations");
var toolErrorCounter = aiMeter.CreateCounter<long>("finops.tool.errors", description: "Tool call errors");
var activeSessionsGauge = aiMeter.CreateUpDownCounter<long>("finops.sessions.active", description: "Currently active chat sessions");
var chatDurationHistogram = aiMeter.CreateHistogram<double>("finops.chat.duration_ms", "ms", "Chat request duration");

var app = builder.Build();
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AzureFinOps.AI");

logger.LogInformation("Application starting. AppInsights configured: {Configured}", !string.IsNullOrEmpty(appInsightsCs));

// Trust forwarded headers from Azure App Service reverse proxy (X-Forwarded-Proto, X-Forwarded-For)
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// HTTPS redirect (production only — App Service terminates TLS at the LB,
// forwarded headers middleware ensures Request.Scheme reflects the original protocol)
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

// Security headers — corporate proxies (Zscaler, Cisco Umbrella, Palo Alto) flag/block
// sites missing these headers as "uncategorized" or "potentially unsafe".
app.Use(async (ctx, next) =>
{
    var headers = ctx.Response.Headers;

    // HSTS — 1 year, include subdomains, allow preload list submission
    if (!app.Environment.IsDevelopment())
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";

    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    // CSP — restrictive but allows the Vue SPA, ECharts (inline styles + canvas data URIs),
    // SSE to self, App Insights browser config, and jsdelivr CDN for ECharts map GeoJSON
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "connect-src 'self' https://cdn.jsdelivr.net https://js.monitor.azure.com https://canadacentral-1.in.applicationinsights.azure.com https://canadacentral.livediagnostics.monitor.azure.com; " +
        "font-src 'self'; " +
        "frame-ancestors 'none'";

    await next();
});

// Redirect www to bare domain so OAuth callbacks and all links use the canonical host
app.Use(async (ctx, next) =>
{
    var host = ctx.Request.Host.Host;
    if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
    {
        var bare = host[4..];
        var port = ctx.Request.Host.Port;
        var newHost = port.HasValue ? $"{bare}:{port}" : bare;
        var url = $"{ctx.Request.Scheme}://{newHost}{ctx.Request.Path}{ctx.Request.QueryString}";
        ctx.Response.Redirect(url, permanent: true);
        return;
    }
    await next();
});

app.UseSession();

// Serve Vue SPA from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

var msClientId = app.Configuration["Microsoft:ClientId"] ?? "";
var msClientSecret = app.Configuration["Microsoft:ClientSecret"] ?? "";
var msTenantId = app.Configuration["Microsoft:TenantId"] ?? "common";
var msHomeTenantId = app.Configuration["Microsoft:HomeTenantId"] ?? msTenantId;

// Auto-assign anonymous session user on first request (no login required for chat)
app.Use(async (ctx, next) =>
{
    if (ctx.Session.GetString("user") is null)
    {
        var sessionUserId = Random.Shared.NextInt64(1_000_000, long.MaxValue);
        ctx.Session.SetString("user", JsonSerializer.Serialize(new
        {
            id = sessionUserId,
            login = $"user-{sessionUserId % 10000:D4}",
            name = (string?)null,
            avatar = (string?)null,
            email = (string?)null
        }));
    }
    await next();
});

// Azure OpenAI configuration
var azureOpenAIEndpoint = app.Configuration["AzureOpenAI:Endpoint"] ?? "https://finops-agent-ai.openai.azure.com/";
var azureOpenAIDeployment = app.Configuration["AzureOpenAI:DeploymentName"] ?? "gpt-5.4";

// Azure OpenAI ChatClient (shared singleton — tools are per-user, bound at agent creation)
#pragma warning disable OPENAI001
var azureOpenAIChatClient = new AzureOpenAIClient(
    new Uri(azureOpenAIEndpoint),
    new ClientSecretCredential(msHomeTenantId, msClientId, msClientSecret))
    .GetChatClient(azureOpenAIDeployment);
#pragma warning restore OPENAI001
logger.LogInformation("Azure OpenAI client created: endpoint={Endpoint} deployment={Deployment}", azureOpenAIEndpoint, azureOpenAIDeployment);

// Per-user AIAgent + AgentSession caches
var userAgents = new ConcurrentDictionary<long, AIAgent>();
var userSessions = new ConcurrentDictionary<long, AgentSession>();
var userTokens = new ConcurrentDictionary<long, UserTokens>();
var userTools = new ConcurrentDictionary<long, List<AIFunction>>();

// Normalize host for OAuth callbacks — strip "www." so callbacks always match
// the registered redirect URIs (e.g. azure-finops-agent.com, not www.azure-finops-agent.com)
string NormalizeCallbackHost(HttpContext ctx)
{
    var host = ctx.Request.Host.ToString();
    if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        host = host[4..];
    return $"{ctx.Request.Scheme}://{host}";
}

// ──────────────────────────────────────────────
// AUTH ENDPOINTS
// ──────────────────────────────────────────────

// Get current user (auto-created anonymous session user, or with Azure identity if connected)
app.MapGet("/auth/me", (HttpContext ctx) =>
{
    var userJson = ctx.Session.GetString("user");
    if (userJson is null)
        return Results.Json(new { id = 0, login = "anonymous" });

    var userObj = JsonSerializer.Deserialize<JsonElement>(userJson);

    // Enrich with Azure identity if available
    var azureUserJson = ctx.Session.GetString("azure_user");
    string? name = null, email = null;
    if (azureUserJson is not null)
    {
        var azureUser = JsonSerializer.Deserialize<JsonElement>(azureUserJson);
        if (azureUser.TryGetProperty("name", out var n)) name = n.GetString();
        if (azureUser.TryGetProperty("email", out var e)) email = e.GetString();
    }

    return Results.Json(new
    {
        id = userObj.GetProperty("id").GetInt64(),
        login = userObj.GetProperty("login").GetString(),
        name = name ?? (userObj.TryGetProperty("name", out var n2) ? n2.GetString() : null),
        email = email ?? (userObj.TryGetProperty("email", out var e2) ? e2.GetString() : null),
    });
});

// Reset session (clear conversation + cached state)
app.MapPost("/auth/logout", (HttpContext ctx) =>
{
    var userJson = ctx.Session.GetString("user");
    if (userJson is not null)
    {
        var u = JsonSerializer.Deserialize<JsonElement>(userJson);
        var uid = u.GetProperty("id").GetInt64();
        userSessions.TryRemove(uid, out _);
        userAgents.TryRemove(uid, out _);
        userTokens.TryRemove(uid, out _);
        userTools.TryRemove(uid, out _);
    }
    ctx.Session.Clear();
    return Results.Ok(new { ok = true });
});

// ──────────────────────────────────────────────
// AZURE / MICROSOFT ENTRA ID OAUTH (Multi-Tenant)
// ──────────────────────────────────────────────

// Redirect to Microsoft Entra ID login
app.MapGet("/auth/microsoft", (HttpContext ctx) =>
{
    if (string.IsNullOrEmpty(msClientId))
        return Results.Problem("Microsoft OAuth is not configured");

    var state = Guid.NewGuid().ToString("N");
    ctx.Session.SetString("ms_oauth_state", state);

    var redirectUri = $"{NormalizeCallbackHost(ctx)}/auth/microsoft/callback";
    var scope = "openid profile email offline_access https://management.azure.com/user_impersonation https://graph.microsoft.com/User.Read https://graph.microsoft.com/Organization.Read.All https://graph.microsoft.com/Directory.Read.All";
    var url = $"https://login.microsoftonline.com/{Uri.EscapeDataString(msTenantId)}/oauth2/v2.0/authorize" +
              $"?client_id={Uri.EscapeDataString(msClientId)}" +
              $"&response_type=code" +
              $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
              $"&scope={Uri.EscapeDataString(scope)}" +
              $"&state={state}" +
              $"&response_mode=query" +
              $"&prompt=select_account";

    logger.LogInformation("Microsoft OAuth redirect initiated from {Host}", ctx.Request.Host);
    return Results.Redirect(url);
});

// Microsoft OAuth callback
app.MapGet("/auth/microsoft/callback", async (HttpContext ctx, IHttpClientFactory httpFactory) =>
{
    try
    {
        var code = ctx.Request.Query["code"].ToString();
        var state = ctx.Request.Query["state"].ToString();
        var error = ctx.Request.Query["error"].ToString();

        if (!string.IsNullOrEmpty(error))
        {
            var errorDesc = ctx.Request.Query["error_description"].ToString();
            logger.LogWarning("Microsoft OAuth error: {Error} — {Description}", error, errorDesc);
            return Results.Redirect("/?azure_error=" + Uri.EscapeDataString(error));
        }

        if (state != ctx.Session.GetString("ms_oauth_state"))
        {
            logger.LogWarning("Microsoft OAuth state mismatch — possible CSRF attempt");
            return Results.StatusCode(403);
        }

        ctx.Session.Remove("ms_oauth_state");

        var http = httpFactory.CreateClient();
        var redirectUri = $"{NormalizeCallbackHost(ctx)}/auth/microsoft/callback";

        using var tokenReq = new HttpRequestMessage(HttpMethod.Post,
            $"https://login.microsoftonline.com/{Uri.EscapeDataString(msTenantId)}/oauth2/v2.0/token");
        tokenReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = msClientId,
            ["client_secret"] = msClientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["scope"] = "openid profile email https://management.azure.com/user_impersonation offline_access"
        });

        var tokenRes = await http.SendAsync(tokenReq);
        var tokenBody = await tokenRes.Content.ReadAsStringAsync();

        if (!tokenRes.IsSuccessStatusCode)
        {
            logger.LogError("Microsoft token exchange failed: status={Status} body={Body}", (int)tokenRes.StatusCode, tokenBody);
            return Results.Redirect("/?azure_error=token_exchange_failed");
        }

        var tokenJson = JsonSerializer.Deserialize<JsonElement>(tokenBody);

        if (!tokenJson.TryGetProperty("access_token", out var atProp))
        {
            logger.LogError("No access_token in Microsoft response");
            return Results.Redirect("/?azure_error=no_access_token");
        }

        var azureToken = atProp.GetString()!;
        var refreshToken = tokenJson.TryGetProperty("refresh_token", out var rtProp) ? rtProp.GetString() : null;
        var expiresIn = tokenJson.TryGetProperty("expires_in", out var expProp) ? expProp.GetInt32() : 3600;

        ctx.Session.SetString("azure_token", azureToken);
        ctx.Session.SetString("azure_token_expiry", DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60).ToString("o"));
        if (refreshToken is not null)
            ctx.Session.SetString("azure_refresh_token", refreshToken);

        // Extract user info from ID token claims (basic info)
        if (tokenJson.TryGetProperty("id_token", out var idTokenProp))
        {
            var idToken = idTokenProp.GetString()!;
            var parts = idToken.Split('.');
            if (parts.Length == 3)
            {
                try
                {
                    var payload = parts[1];
                    // Pad base64url
                    payload = payload.Replace('-', '+').Replace('_', '/');
                    switch (payload.Length % 4)
                    {
                        case 2: payload += "=="; break;
                        case 3: payload += "="; break;
                    }
                    var claims = JsonSerializer.Deserialize<JsonElement>(Convert.FromBase64String(payload));
                    var azureUser = new Dictionary<string, string?>();
                    if (claims.TryGetProperty("name", out var n)) azureUser["name"] = n.GetString();
                    if (claims.TryGetProperty("preferred_username", out var u)) azureUser["email"] = u.GetString();
                    if (claims.TryGetProperty("tid", out var t)) azureUser["tenantId"] = t.GetString();
                    if (claims.TryGetProperty("oid", out var o)) azureUser["objectId"] = o.GetString();
                    ctx.Session.SetString("azure_user", JsonSerializer.Serialize(azureUser));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to parse ID token claims");
                }
            }
        }

        logger.LogInformation("Microsoft OAuth login successful");

        // Exchange refresh token for Graph + Log Analytics tokens
        if (refreshToken is not null)
        {
            // Graph token
            try
            {
                var graphToken = await ExchangeRefreshTokenForResource(http, refreshToken, "https://graph.microsoft.com/.default");
                if (graphToken is not null)
                {
                    ctx.Session.SetString("graph_token", graphToken.Value.Token);
                    ctx.Session.SetString("graph_token_expiry", graphToken.Value.Expiry.ToString("o"));
                }
            }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to get Graph token"); }

            // Log Analytics token (also works for App Insights query API)
            try
            {
                var laToken = await ExchangeRefreshTokenForResource(http, refreshToken, "https://api.loganalytics.io/.default");
                if (laToken is not null)
                {
                    ctx.Session.SetString("loganalytics_token", laToken.Value.Token);
                    ctx.Session.SetString("loganalytics_token_expiry", laToken.Value.Expiry.ToString("o"));
                }
            }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to get Log Analytics token"); }
        }

        return Results.Redirect("/");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Microsoft OAuth callback failed");
        return Results.Redirect("/?azure_error=callback_failed");
    }
});

// Helper: refresh Azure token silently
async Task<string?> RefreshAzureTokenAsync(HttpContext ctx, IHttpClientFactory httpFactory)
{
    var refreshToken = ctx.Session.GetString("azure_refresh_token");
    if (refreshToken is null) return null;

    var http = httpFactory.CreateClient();
    using var req = new HttpRequestMessage(HttpMethod.Post,
        $"https://login.microsoftonline.com/{Uri.EscapeDataString(msTenantId)}/oauth2/v2.0/token");
    req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["client_id"] = msClientId,
        ["client_secret"] = msClientSecret,
        ["refresh_token"] = refreshToken,
        ["grant_type"] = "refresh_token",
        ["scope"] = "openid profile email https://management.azure.com/user_impersonation offline_access"
    });

    var res = await http.SendAsync(req);
    if (!res.IsSuccessStatusCode)
    {
        logger.LogWarning("Azure token refresh failed: status={Status}", (int)res.StatusCode);
        return null;
    }

    var body = await res.Content.ReadAsStringAsync();
    var json = JsonSerializer.Deserialize<JsonElement>(body);
    if (!json.TryGetProperty("access_token", out var newToken))
    {
        logger.LogWarning("Azure token refresh: no access_token in response");
        return null;
    }

    var newAccessToken = newToken.GetString()!;
    var expiresIn = json.TryGetProperty("expires_in", out var expProp) ? expProp.GetInt32() : 3600;
    ctx.Session.SetString("azure_token", newAccessToken);
    ctx.Session.SetString("azure_token_expiry", DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60).ToString("o"));
    if (json.TryGetProperty("refresh_token", out var newRt))
        ctx.Session.SetString("azure_refresh_token", newRt.GetString()!);

    logger.LogInformation("Azure token refreshed successfully");
    return newAccessToken;
}

// Helper: exchange refresh token for a token scoped to a specific resource
async Task<(string Token, DateTimeOffset Expiry)?> ExchangeRefreshTokenForResource(HttpClient http, string refreshToken, string scope)
{
    using var req = new HttpRequestMessage(HttpMethod.Post,
        $"https://login.microsoftonline.com/{Uri.EscapeDataString(msTenantId)}/oauth2/v2.0/token");
    req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["client_id"] = msClientId,
        ["client_secret"] = msClientSecret,
        ["refresh_token"] = refreshToken,
        ["grant_type"] = "refresh_token",
        ["scope"] = scope
    });

    var res = await http.SendAsync(req);
    if (!res.IsSuccessStatusCode) return null;

    var body = await res.Content.ReadAsStringAsync();
    var json = JsonSerializer.Deserialize<JsonElement>(body);
    if (!json.TryGetProperty("access_token", out var tokenProp)) return null;

    var expiresIn = json.TryGetProperty("expires_in", out var expProp) ? expProp.GetInt32() : 3600;
    return (tokenProp.GetString()!, DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60));
}

// Helper: get valid Graph token
async Task<string?> GetGraphTokenAsync(HttpContext ctx, IHttpClientFactory httpFactory)
{
    var token = ctx.Session.GetString("graph_token");
    if (token is null) return null;

    var expiryStr = ctx.Session.GetString("graph_token_expiry");
    if (expiryStr is not null && DateTimeOffset.TryParse(expiryStr, out var expiry) && expiry <= DateTimeOffset.UtcNow)
    {
        var refreshToken = ctx.Session.GetString("azure_refresh_token");
        if (refreshToken is null) return null;
        var http = httpFactory.CreateClient();
        var result = await ExchangeRefreshTokenForResource(http, refreshToken, "https://graph.microsoft.com/.default");
        if (result is null) return null;
        ctx.Session.SetString("graph_token", result.Value.Token);
        ctx.Session.SetString("graph_token_expiry", result.Value.Expiry.ToString("o"));
        return result.Value.Token;
    }
    return token;
}

// Helper: get valid Log Analytics token (also works for App Insights query API)
async Task<string?> GetLogAnalyticsTokenAsync(HttpContext ctx, IHttpClientFactory httpFactory)
{
    var token = ctx.Session.GetString("loganalytics_token");
    if (token is null) return null;

    var expiryStr = ctx.Session.GetString("loganalytics_token_expiry");
    if (expiryStr is not null && DateTimeOffset.TryParse(expiryStr, out var expiry) && expiry <= DateTimeOffset.UtcNow)
    {
        var refreshToken = ctx.Session.GetString("azure_refresh_token");
        if (refreshToken is null) return null;
        var http = httpFactory.CreateClient();
        var result = await ExchangeRefreshTokenForResource(http, refreshToken, "https://api.loganalytics.io/.default");
        if (result is null) return null;
        ctx.Session.SetString("loganalytics_token", result.Value.Token);
        ctx.Session.SetString("loganalytics_token_expiry", result.Value.Expiry.ToString("o"));
        return result.Value.Token;
    }
    return token;
}

// Helper: get valid Azure token (auto-refresh if expired)
async Task<string?> GetAzureTokenAsync(HttpContext ctx, IHttpClientFactory httpFactory)
{
    var token = ctx.Session.GetString("azure_token");
    if (token is null) return null;

    var expiryStr = ctx.Session.GetString("azure_token_expiry");
    if (expiryStr is not null && DateTimeOffset.TryParse(expiryStr, out var expiry) && expiry <= DateTimeOffset.UtcNow)
    {
        token = await RefreshAzureTokenAsync(ctx, httpFactory);
    }
    return token;
}

// Azure connection status + subscription discovery
app.MapGet("/auth/azure/status", async (HttpContext ctx, IHttpClientFactory httpFactory) =>
{
    var token = await GetAzureTokenAsync(ctx, httpFactory);
    if (token is null)
        return Results.Json(new { connected = false });

    var azureUserJson = ctx.Session.GetString("azure_user");
    object? azureUser = azureUserJson is not null ? JsonSerializer.Deserialize<JsonElement>(azureUserJson) : null;

    // Discover subscriptions — create a scoped HttpClient from the factory (not shared)
    // and set auth on individual requests to avoid cross-user token leakage
    var http = httpFactory.CreateClient();

    var subscriptions = new List<object>();
    try
    {
        using var subReq = new HttpRequestMessage(HttpMethod.Get, "https://management.azure.com/subscriptions?api-version=2022-12-01");
        subReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        subReq.Headers.Add("User-Agent", "FinOps-Dashboard/1.0");
        var subRes = await http.SendAsync(subReq);
        var subBody = await subRes.Content.ReadAsStringAsync();
        var subJson = JsonSerializer.Deserialize<JsonElement>(subBody);
        if (subJson.TryGetProperty("value", out var subs))
        {
            foreach (var sub in subs.EnumerateArray())
            {
                subscriptions.Add(new
                {
                    id = sub.GetProperty("subscriptionId").GetString(),
                    name = sub.GetProperty("displayName").GetString(),
                    state = sub.GetProperty("state").GetString()
                });
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to list Azure subscriptions");
    }

    // Discover management groups
    var managementGroups = new List<object>();
    try
    {
        using var mgReq = new HttpRequestMessage(HttpMethod.Get, "https://management.azure.com/providers/Microsoft.Management/managementGroups?api-version=2021-04-01");
        mgReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        mgReq.Headers.Add("User-Agent", "FinOps-Dashboard/1.0");
        var mgRes = await http.SendAsync(mgReq);
        var mgBody = await mgRes.Content.ReadAsStringAsync();
        var mgJson = JsonSerializer.Deserialize<JsonElement>(mgBody);
        if (mgJson.TryGetProperty("value", out var mgs))
        {
            foreach (var mg in mgs.EnumerateArray())
            {
                managementGroups.Add(new
                {
                    id = mg.GetProperty("id").GetString(),
                    name = mg.TryGetProperty("properties", out var props) && props.TryGetProperty("displayName", out var dn) ? dn.GetString() : mg.GetProperty("name").GetString()
                });
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to list management groups");
    }

    var connectedApis = new List<string>
    {
        "Cost Management",
        "Billing",
        "Advisor",
        "Resource Graph",
        "Azure Monitor",
        "Resource Health",
        "Subscriptions"
    };

    return Results.Json(new
    {
        connected = true,
        user = azureUser,
        subscriptions,
        managementGroups,
        apis = connectedApis
    });
});

// Disconnect Azure
app.MapPost("/auth/azure/disconnect", (HttpContext ctx) =>
{
    // Clear per-user cached tokens
    var userJson = ctx.Session.GetString("user");
    if (userJson is not null)
    {
        var u = JsonSerializer.Deserialize<JsonElement>(userJson);
        var uid = u.GetProperty("id").GetInt64();
        if (userTokens.TryGetValue(uid, out var tokens))
        {
            tokens.AzureToken = null;
            tokens.GraphToken = null;
            tokens.LogAnalyticsToken = null;
        }
        logger.LogInformation("Azure disconnected for user {UserId}", uid);
    }
    else
    {
        logger.LogInformation("Azure disconnected (no user context)");
    }

    ctx.Session.Remove("azure_token");
    ctx.Session.Remove("azure_refresh_token");
    ctx.Session.Remove("azure_token_expiry");
    ctx.Session.Remove("azure_user");
    ctx.Session.Remove("graph_token");
    ctx.Session.Remove("graph_token_expiry");
    ctx.Session.Remove("loganalytics_token");
    ctx.Session.Remove("loganalytics_token_expiry");
    return Results.Ok(new { ok = true });
});

// ──────────────────────────────────────────────
// CHAT SSE ENDPOINT (Azure OpenAI via Microsoft Agent Framework)
// ──────────────────────────────────────────────

// System prompt for the FinOps Agent
const string SystemPrompt = @"
You are the Azure FinOps Agent — a concise, data-driven AI assistant for Azure cost optimization.

## Rules
- Be concise. Answer directly in 1-3 sentences when possible.
- Use tables ONLY for structured data. Keep columns minimal.
- Max ONE chart per response. Only chart when asked or when it clearly adds value.
- If the user has not connected Azure, tell them to click 'Connect Azure' in the sidebar.
- FetchUrl, QueryAzure, QueryGraph, QueryLogAnalytics return truncated previews (300 chars). Full data is saved to a file — use ReadFile on the path shown, or use RunScript with Python `requests` to fetch data directly.
- RunScript returns full output (not truncated). Prefer RunScript for multi-step data processing, chart data prep, and API calls that need full responses.
- When building visualizations, prefer a single RunScript call that fetches + processes + prints chart-ready JSON, then pass it to RenderAdvancedChart — this avoids multiple round trips.
- Call multiple tools in parallel when they are independent (e.g. QueryAzure + QueryGraph simultaneously) to reduce latency.
";

// ── Shared (stateless) AI tools — safe to share across all users ──
var sharedTools = new List<AIFunction>();
var chartLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AzureFinOps.AI.Charts");
sharedTools.AddRange(ChartTools.Create(chartLogger));
sharedTools.AddRange(PricingTools.Create());
sharedTools.AddRange(HealthTools.Create());
sharedTools.AddRange(PresentationTools.Create());
sharedTools.AddRange(FileSystemTools.Create());
sharedTools.AddRange(SearchTools.Create());
sharedTools.AddRange(WebFetchTools.Create());
sharedTools.AddRange(FollowUpTools.Create());

// Per-user token holders and tool lists — tools capture the UserTokens instance
// via closure, so they always read the latest tokens regardless of thread.

List<AIFunction> GetOrCreateUserTools(long userId)
{
    return userTools.GetOrAdd(userId, uid =>
    {
        var tokens = userTokens.GetOrAdd(uid, _ => new UserTokens());
        var tools = new List<AIFunction>(sharedTools);
        tools.AddRange(new CodeExecutionTools(tokens).Create());
        tools.AddRange(new AzureQueryTools(tokens).Create());
        tools.AddRange(new GraphQueryTools(tokens).Create());
        tools.AddRange(new LogAnalyticsQueryTools(tokens).Create());
        tools.AddRange(new MemoryTools(uid).Create()); // Per-user memory
        return tools;
    });
}

// Get or create per-user AIAgent (Chat Completions API — MAF manages history via InMemoryChatHistoryProvider)
AIAgent GetOrCreateAgent(long userId)
{
    return userAgents.GetOrAdd(userId, uid =>
        azureOpenAIChatClient.AsIChatClient()
            .AsBuilder()
            .ConfigureOptions(opts => opts.Reasoning = new() { Effort = ReasoningEffort.High })
            .UseFunctionInvocation(null, f => f.AllowConcurrentInvocation = true)
            .Build()
            .AsAIAgent(
                instructions: SystemPrompt,
                name: "AzureFinOpsAgent",
                tools: GetOrCreateUserTools(uid).Cast<AITool>().ToList()));
}

app.MapPost("/api/chat", (Delegate)(async (HttpContext ctx, IHttpClientFactory httpFactory) =>
{
    var userJson = ctx.Session.GetString("user");
    if (userJson is null)
    {
        ctx.Response.StatusCode = 401;
        return;
    }

    using var bodyDoc = await JsonDocument.ParseAsync(ctx.Request.Body);
    var prompt = bodyDoc.RootElement.GetProperty("prompt").GetString();

    if (string.IsNullOrWhiteSpace(prompt))
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { error = "prompt is required" });
        return;
    }

    var user = JsonSerializer.Deserialize<JsonElement>(userJson);
    var userId = user.GetProperty("id").GetInt64();
    var userLogin = user.TryGetProperty("login", out var loginProp) ? loginProp.GetString() : userId.ToString();

    var chatSw = Stopwatch.StartNew();
    chatRequestCounter.Add(1, new KeyValuePair<string, object?>("model", azureOpenAIDeployment), new KeyValuePair<string, object?>("user", userLogin));

    using var chatActivity = aiActivitySource.StartActivity("ChatRequest");
    chatActivity?.SetTag("ai.user", userLogin);
    chatActivity?.SetTag("ai.model", azureOpenAIDeployment);
    chatActivity?.SetTag("ai.prompt_length", prompt!.Length);
    chatActivity?.SetTag("ai.prompt", prompt.Length > 500 ? prompt[..500] + "..." : prompt);
    logger.LogInformation("Chat request from {User} model={Model} promptLen={PromptLen}", userLogin, azureOpenAIDeployment, prompt.Length);

    // Update per-user token holder with fresh Azure/Graph/LA tokens
    var tokens = userTokens.GetOrAdd(userId, _ => new UserTokens());
    await tokens.RefreshLock.WaitAsync(ctx.RequestAborted);
    try
    {
        tokens.AzureToken = await GetAzureTokenAsync(ctx, httpFactory);
        tokens.GraphToken = await GetGraphTokenAsync(ctx, httpFactory);
        tokens.LogAnalyticsToken = await GetLogAnalyticsTokenAsync(ctx, httpFactory);
    }
    finally
    {
        tokens.RefreshLock.Release();
    }

    logger.LogInformation("Chat tokens: azure={HasAzure} graph={HasGraph} la={HasLA}",
        tokens.AzureToken is not null, tokens.GraphToken is not null, tokens.LogAnalyticsToken is not null);

    // SSE headers
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";

    try
    {
        var agent = GetOrCreateAgent(userId);

        // Get or create per-user session (maintains conversation history via MAF)
        if (!userSessions.TryGetValue(userId, out var session))
        {
            session = await agent.CreateSessionAsync();
            userSessions[userId] = session;
            logger.LogInformation("Created new agent session for {User}", userLogin);
        }

        // Track active tool calls for SSE events
        var activeToolCalls = new Dictionary<string, (string Name, Stopwatch Timer, Activity? Activity)>();

        // Stream response — MAF handles the entire agentic loop (tool calls + results) automatically
        await foreach (var update in agent.RunStreamingAsync(prompt, session))
        {
            if (ctx.RequestAborted.IsCancellationRequested) break;

            // Stream text deltas
            if (update.Text is { Length: > 0 })
            {
                var deltaData = JsonSerializer.Serialize(new { type = "delta", content = update.Text });
                await ctx.Response.WriteAsync($"data: {deltaData}\n\n");
                await ctx.Response.Body.FlushAsync();
            }

            // Detect tool calls and results from streaming contents
            if (update.Contents is not null)
            {
                foreach (var content in update.Contents)
                {
                    // Tool call started
                    if (content is FunctionCallContent fcc)
                    {
                        var toolId = fcc.CallId ?? Guid.NewGuid().ToString();
                        string? argsJson = null;
                        try { argsJson = fcc.Arguments is not null ? JsonSerializer.Serialize(fcc.Arguments) : null; } catch { }

                        toolCallCounter.Add(1, new KeyValuePair<string, object?>("tool", fcc.Name), new KeyValuePair<string, object?>("user", userLogin));
                        var toolActivity = aiActivitySource.StartActivity($"Tool:{fcc.Name}");
                        toolActivity?.SetTag("ai.tool.name", fcc.Name);
                        toolActivity?.SetTag("ai.tool.id", toolId);
                        toolActivity?.SetTag("ai.tool.args", argsJson?.Length > 1000 ? argsJson[..1000] + "..." : argsJson);
                        activeToolCalls[toolId] = (fcc.Name, Stopwatch.StartNew(), toolActivity);
                        logger.LogInformation("Tool start: {Tool} id={ToolId} args={Args}", fcc.Name, toolId, argsJson ?? "(none)");

                        var startData = JsonSerializer.Serialize(new { type = "tool_start", tool = fcc.Name, id = toolId, args = argsJson });
                        await ctx.Response.WriteAsync($"data: {startData}\n\n");
                        await ctx.Response.Body.FlushAsync();
                    }

                    // Tool result returned (MAF already invoked the tool)
                    if (content is FunctionResultContent frc)
                    {
                        var toolId = frc.CallId ?? "";
                        var resultText = frc.Result?.ToString();
                        var toolName = activeToolCalls.TryGetValue(toolId, out var info) ? info.Name : "unknown";
                        var durationMs = info.Timer?.ElapsedMilliseconds ?? 0;
                        var success = resultText is not null && !resultText.StartsWith("Error");

                        // Complete telemetry span
                        if (activeToolCalls.Remove(toolId, out var removed))
                        {
                            removed.Timer.Stop();
                            removed.Activity?.SetTag("ai.tool.success", success);
                            removed.Activity?.SetTag("ai.tool.durationMs", removed.Timer.ElapsedMilliseconds);
                            removed.Activity?.Dispose();
                        }
                        if (!success) toolErrorCounter.Add(1, new KeyValuePair<string, object?>("tool", toolName), new KeyValuePair<string, object?>("user", userLogin));

                        logger.LogInformation("Tool done: {Tool} id={ToolId} success={Success} durationMs={Duration} resultLen={ResultLen}",
                            toolName, toolId, success, durationMs, resultText?.Length ?? 0);
                        logger.LogDebug("Tool output: {Tool} id={ToolId} result={Result}",
                            toolName, toolId, resultText?.Length > 2000 ? resultText[..2000] + "...(truncated)" : resultText ?? "(null)");

                        // Emit tool_done SSE
                        var errorText = success ? null : resultText;
                        var doneData = JsonSerializer.Serialize(new { type = "tool_done", tool = toolName, id = toolId, success, durationMs, result = resultText, error = errorText });
                        await ctx.Response.WriteAsync($"data: {doneData}\n\n");
                        await ctx.Response.Body.FlushAsync();

                        // Chart detection (RenderChart / RenderAdvancedChart)
                        if ((toolName == "RenderChart" || toolName == "RenderAdvancedChart") && success && resultText is not null)
                        {
                            var chartData = JsonSerializer.Serialize(new { type = "chart", options = resultText });
                            await ctx.Response.WriteAsync($"data: {chartData}\n\n");
                            await ctx.Response.Body.FlushAsync();
                        }
                        else if (success && resultText is not null && resultText.Contains("__CHART__:"))
                        {
                            foreach (var line in resultText.Split('\n'))
                            {
                                var trimmed = line.Trim();
                                if (trimmed.StartsWith("__CHART__:"))
                                {
                                    var chartJson = trimmed["__CHART__:".Length..].Trim();
                                    var chartPayload = JsonSerializer.Serialize(new { type = "chart", options = chartJson });
                                    await ctx.Response.WriteAsync($"data: {chartPayload}\n\n");
                                    await ctx.Response.Body.FlushAsync();
                                    break;
                                }
                            }
                        }

                        // PPTX detection
                        if (success && resultText is not null && resultText.Contains("__PPTX_READY__:"))
                        {
                            foreach (var line in resultText.Split('\n'))
                            {
                                var trimmed = line.Trim();
                                if (trimmed.StartsWith("__PPTX_READY__:"))
                                {
                                    var parts = trimmed["__PPTX_READY__:".Length..].Split(':', 3);
                                    if (parts.Length >= 2)
                                    {
                                        var pptxPayload = JsonSerializer.Serialize(new { type = "pptx_ready", fileId = parts[0], fileName = parts[1], slideCount = parts.Length > 2 ? parts[2] : "" });
                                        await ctx.Response.WriteAsync($"data: {pptxPayload}\n\n");
                                        await ctx.Response.Body.FlushAsync();
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        // Done
        await ctx.Response.WriteAsync("data: [DONE]\n\n");
        await ctx.Response.Body.FlushAsync();

        chatSw.Stop();
        chatDurationHistogram.Record(chatSw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("model", azureOpenAIDeployment), new KeyValuePair<string, object?>("user", userLogin));
        chatActivity?.SetTag("ai.duration_ms", chatSw.Elapsed.TotalMilliseconds);
    }
    catch (Exception ex)
    {
        chatSw.Stop();
        chatErrorCounter.Add(1, new KeyValuePair<string, object?>("model", azureOpenAIDeployment), new KeyValuePair<string, object?>("user", userLogin), new KeyValuePair<string, object?>("error_type", ex.GetType().Name));
        chatActivity?.SetTag("ai.error", ex.Message);
        chatActivity?.SetTag("ai.error_type", ex.GetType().Name);
        logger.LogError(ex, "Chat request failed for {User}", userLogin);
        var errorData = JsonSerializer.Serialize(new { type = "error", message = ex.Message });
        await ctx.Response.WriteAsync($"data: {errorData}\n\n");
        await ctx.Response.WriteAsync("data: [DONE]\n\n");
        await ctx.Response.Body.FlushAsync();
    }
}));

// ──────────────────────────────────────────────
// RESET (NEW THREAD)
// ──────────────────────────────────────────────

app.MapPost("/api/chat/reset", async (HttpContext ctx) =>
{
    var userJson = ctx.Session.GetString("user");
    if (userJson is null) { ctx.Response.StatusCode = 401; return; }

    var user = JsonSerializer.Deserialize<JsonElement>(userJson);
    var userId = user.GetProperty("id").GetInt64();

    // Drop the AgentSession — InMemoryChatHistoryProvider holds conversation history.
    // Creating a new session starts a fresh conversation thread.
    userSessions.TryRemove(userId, out _);

    // Clear persistent per-user memory (MemoryTools stores facts to disk that survive sessions)
    MemoryTools.ClearMemory(userId);

    logger.LogInformation("Agent session and memory cleared for user {UserId}", userId);

    ctx.Response.StatusCode = 204;
});

// ──────────────────────────────────────────────
// MODELS ENDPOINT
// ──────────────────────────────────────────────

app.MapGet("/api/models", (HttpContext ctx) =>
{
    var userJson = ctx.Session.GetString("user");
    if (userJson is null)
    {
        ctx.Response.StatusCode = 401;
        return Results.Unauthorized();
    }

    // Return the configured Azure OpenAI deployment
    return Results.Json(new[]
    {
        new { id = azureOpenAIDeployment, name = azureOpenAIDeployment }
    });
});

// ──────────────────────────────────────────────
// VERSION / BUILD INFO
// ──────────────────────────────────────────────
var buildSha = Environment.GetEnvironmentVariable("BUILD_SHA");
if (string.IsNullOrEmpty(buildSha))
{
    try { buildSha = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "rev-parse --short HEAD") { RedirectStandardOutput = true, UseShellExecute = false })!.StandardOutput.ReadToEnd().Trim(); }
    catch { buildSha = "dev"; }
}
var buildNumber = Environment.GetEnvironmentVariable("BUILD_NUMBER");
if (string.IsNullOrEmpty(buildNumber))
{
    try { buildNumber = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "rev-list --count HEAD") { RedirectStandardOutput = true, UseShellExecute = false })!.StandardOutput.ReadToEnd().Trim(); }
    catch { buildNumber = "0"; }
}
app.MapGet("/api/version", () => Results.Ok(new { sha = buildSha, build = buildNumber, started = DateTime.UtcNow.ToString("o") }));

// ──────────────────────────────────────────────
// FRONTEND CONFIG (App Insights connection string)
// ──────────────────────────────────────────────
app.MapGet("/api/config", () => Results.Ok(new { appInsightsConnectionString = appInsightsCs ?? "" }));

// ──────────────────────────────────────────────
// PRESENTATION DOWNLOAD
// ──────────────────────────────────────────────
app.MapGet("/api/download/pptx/{fileId}", (string fileId, HttpContext ctx) =>
{
    if (!PresentationTools.GeneratedFiles.TryGetValue(fileId, out var entry))
        return Results.NotFound(new { error = "File not found or expired" });

    if (!File.Exists(entry.Path))
    {
        PresentationTools.GeneratedFiles.TryRemove(fileId, out _);
        return Results.NotFound(new { error = "File no longer available" });
    }

    var fileName = Path.GetFileName(entry.Path);
    // Remove the fileId prefix from the download name
    var downloadName = fileName.Contains('_') ? fileName[(fileName.IndexOf('_') + 1)..] : fileName;
    var bytes = File.ReadAllBytes(entry.Path);

    // Clean up after serving
    try { File.Delete(entry.Path); } catch { }
    PresentationTools.GeneratedFiles.TryRemove(fileId, out _);

    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.presentationml.presentation", downloadName);
});

// ──────────────────────────────────────────────
// SPA FALLBACK
// ──────────────────────────────────────────────
app.MapFallbackToFile("index.html");

app.Run();

