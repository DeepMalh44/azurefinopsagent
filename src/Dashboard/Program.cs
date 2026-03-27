using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using GitHub.Copilot.SDK;
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
var sessionCreatedCounter = aiMeter.CreateCounter<long>("finops.session.created", description: "Copilot sessions created");
var sessionExpiredCounter = aiMeter.CreateCounter<long>("finops.session.expired", description: "Copilot sessions expired and recreated");
var tokenRefreshCounter = aiMeter.CreateCounter<long>("finops.auth.token_refreshes", description: "GitHub token refreshes");
var tokenRefreshFailCounter = aiMeter.CreateCounter<long>("finops.auth.token_refresh_failures", description: "GitHub token refresh failures");
var authCounter = aiMeter.CreateCounter<long>("finops.auth.logins", description: "Successful GitHub logins");
var toolCallCounter = aiMeter.CreateCounter<long>("finops.tool.calls", description: "Tool call invocations");
var toolErrorCounter = aiMeter.CreateCounter<long>("finops.tool.errors", description: "Tool call errors");
var activeSessionsGauge = aiMeter.CreateUpDownCounter<long>("finops.sessions.active", description: "Currently active Copilot sessions");
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
    // GitHub avatars, SSE to self, GitHub OAuth flow, App Insights browser config,
    // and jsdelivr CDN for ECharts map GeoJSON
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https://avatars.githubusercontent.com; " +
        "connect-src 'self' https://github.com https://api.github.com https://cdn.jsdelivr.net https://js.monitor.azure.com https://canadacentral-1.in.applicationinsights.azure.com https://canadacentral.livediagnostics.monitor.azure.com; " +
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

var gitHubClientId = app.Configuration["GitHub:ClientId"]!;
var gitHubClientSecret = app.Configuration["GitHub:ClientSecret"]!;

var msClientId = app.Configuration["Microsoft:ClientId"] ?? "";
var msClientSecret = app.Configuration["Microsoft:ClientSecret"] ?? "";
var msTenantId = app.Configuration["Microsoft:TenantId"] ?? "common";

// Per-user CopilotClient + CopilotSession cache (declared early so auth endpoints can reference them)
var clients = new ConcurrentDictionary<long, CopilotClient>();
var sessions = new ConcurrentDictionary<long, CopilotSession>();
var clientTokens = new ConcurrentDictionary<long, string>();
var sessionModels = new ConcurrentDictionary<long, string>();
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

// Redirect to GitHub OAuth
app.MapGet("/auth/github", (HttpContext ctx) =>
{
    var state = Guid.NewGuid().ToString("N");
    ctx.Session.SetString("oauth_state", state);

    var callbackUrl = $"{NormalizeCallbackHost(ctx)}/auth/github/callback";
    // GitHub App OAuth — permissions are configured on the app itself, not via scopes.
    // This avoids the classic OAuth App consent screen that shows "access all repos".
    var url = $"https://github.com/login/oauth/authorize?client_id={gitHubClientId}&redirect_uri={Uri.EscapeDataString(callbackUrl)}&state={state}";
    logger.LogInformation("OAuth redirect initiated from {Host}", ctx.Request.Host);
    return Results.Redirect(url);
});

// GitHub OAuth callback
app.MapGet("/auth/github/callback", async (HttpContext ctx, IHttpClientFactory httpFactory) =>
{
    using var authActivity = aiActivitySource.StartActivity("GitHubOAuthCallback");
    try
    {
        var code = ctx.Request.Query["code"].ToString();
        var state = ctx.Request.Query["state"].ToString();

        if (state != ctx.Session.GetString("oauth_state"))
        {
            logger.LogWarning("OAuth state mismatch — possible CSRF attempt");
            authActivity?.SetTag("auth.result", "state_mismatch");
            return Results.StatusCode(403);
        }

        ctx.Session.Remove("oauth_state");

        var http = httpFactory.CreateClient();

        // Exchange code for token
        using var tokenReq = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token");
        tokenReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        tokenReq.Content = JsonContent.Create(new
        {
            client_id = gitHubClientId,
            client_secret = gitHubClientSecret,
            code
        });

        var tokenRes = await http.SendAsync(tokenReq);
        var tokenBody = await tokenRes.Content.ReadAsStringAsync();

        if (!tokenRes.IsSuccessStatusCode)
        {
            logger.LogError("GitHub token exchange failed: status={Status} body={Body}", (int)tokenRes.StatusCode, tokenBody);
            authActivity?.SetTag("auth.result", "token_exchange_failed");
            return Results.Problem($"GitHub token exchange returned {(int)tokenRes.StatusCode}: {tokenBody}");
        }

        var tokenJson = JsonSerializer.Deserialize<JsonElement>(tokenBody);

        if (tokenJson.TryGetProperty("error", out var err))
        {
            logger.LogError("OAuth token exchange error: {Error}", err);
            authActivity?.SetTag("auth.result", "oauth_error");
            return Results.Problem($"OAuth token exchange failed: {err}");
        }

        if (!tokenJson.TryGetProperty("access_token", out var tokenProp))
        {
            logger.LogError("No access_token in GitHub response");
            authActivity?.SetTag("auth.result", "no_access_token");
            return Results.Problem($"No access_token in GitHub response: {tokenBody}");
        }

        var accessToken = tokenProp.GetString()!;

        // Store refresh token and expiry if provided (GitHub App tokens expire after ~8h)
        var refreshToken = tokenJson.TryGetProperty("refresh_token", out var rtProp) ? rtProp.GetString() : null;
        var ghExpiresIn = tokenJson.TryGetProperty("expires_in", out var ghExpProp) ? ghExpProp.GetInt32() : 0;
        authActivity?.SetTag("auth.has_refresh_token", refreshToken is not null);
        authActivity?.SetTag("auth.expires_in", ghExpiresIn);

        // Validate token scopes
        using var scopeReq = new HttpRequestMessage(HttpMethod.Head, "https://api.github.com/user");
        scopeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        scopeReq.Headers.UserAgent.ParseAdd("FinOps-Dashboard/1.0");
        var scopeRes = await http.SendAsync(scopeReq);
        var grantedScopes = scopeRes.Headers.Contains("X-OAuth-Scopes")
            ? scopeRes.Headers.GetValues("X-OAuth-Scopes").FirstOrDefault() ?? ""
            : "";
        logger.LogInformation("OAuth token scopes granted: {Scopes}", grantedScopes);
        ctx.Session.SetString("token_scopes", grantedScopes);

        // Fetch user profile
        using var userReq = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
        userReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        userReq.Headers.UserAgent.ParseAdd("FinOps-Dashboard/1.0");

        var userRes = await http.SendAsync(userReq);
        var userBody = await userRes.Content.ReadAsStringAsync();

        if (!userRes.IsSuccessStatusCode)
        {
            logger.LogError("GitHub user API failed: status={Status}", (int)userRes.StatusCode);
            authActivity?.SetTag("auth.result", "user_api_failed");
            return Results.Problem($"GitHub user API returned {(int)userRes.StatusCode}: {userBody}");
        }

        var userJson = JsonSerializer.Deserialize<JsonElement>(userBody);
        var login = userJson.GetProperty("login").GetString()!;
        logger.LogInformation("GitHub OAuth login successful for {User}", login);
        authActivity?.SetTag("auth.result", "success");
        authActivity?.SetTag("auth.user", login);
        authCounter.Add(1, new KeyValuePair<string, object?>("user", login));

        // Store in session
        ctx.Session.SetString("github_token", accessToken);
        if (ghExpiresIn > 0)
            ctx.Session.SetString("github_token_expiry", DateTimeOffset.UtcNow.AddSeconds(ghExpiresIn - 300).ToString("o"));
        if (refreshToken is not null)
            ctx.Session.SetString("github_refresh_token", refreshToken);
        ctx.Session.SetString("user", JsonSerializer.Serialize(new
        {
            id = userJson.GetProperty("id").GetInt64(),
            login,
            name = userJson.TryGetProperty("name", out var n) ? n.GetString() : null,
            avatar = userJson.GetProperty("avatar_url").GetString(),
            email = userJson.TryGetProperty("email", out var e) ? e.GetString() : null
        }));

        return Results.Redirect("/");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "OAuth callback failed");
        authActivity?.SetTag("auth.result", "exception");
        authActivity?.SetTag("auth.error", ex.Message);
        return Results.Problem($"OAuth callback failed: {ex.Message}");
    }
});

// Get current user (includes token scopes for frontend display)
app.MapGet("/auth/me", (HttpContext ctx) =>
{
    var userJson = ctx.Session.GetString("user");
    if (userJson is null)
        return Results.Unauthorized();

    var scopes = ctx.Session.GetString("token_scopes") ?? "";
    var userObj = JsonSerializer.Deserialize<JsonElement>(userJson);

    var response = new Dictionary<string, object?>
    {
        ["id"] = userObj.GetProperty("id").GetInt64(),
        ["login"] = userObj.GetProperty("login").GetString(),
        ["name"] = userObj.TryGetProperty("name", out var n2) ? n2.GetString() : null,
        ["avatar"] = userObj.GetProperty("avatar").GetString(),
        ["email"] = userObj.TryGetProperty("email", out var e2) ? e2.GetString() : null,
        ["scopes"] = scopes,
    };
    return Results.Json(response);
});

// Logout
app.MapPost("/auth/logout", async (HttpContext ctx) =>
{
    var userJson = ctx.Session.GetString("user");
    if (userJson is not null)
    {
        var u = JsonSerializer.Deserialize<JsonElement>(userJson);
        var uid = u.GetProperty("id").GetInt64();
        var uLogin = u.TryGetProperty("login", out var lp) ? lp.GetString() : uid.ToString();
        logger.LogInformation("User {User} logged out", uLogin);

        // Clear cached CopilotClient, session, and per-user tokens
        if (sessions.TryRemove(uid, out var oldSession))
        {
            activeSessionsGauge.Add(-1);
            try { await oldSession.DisposeAsync(); } catch { }
        }
        sessionModels.TryRemove(uid, out _);
        if (clients.TryRemove(uid, out var oldClient))
            try { oldClient.Dispose(); } catch { }
        clientTokens.TryRemove(uid, out _);
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
// CHAT SSE ENDPOINT (Copilot SDK)
// ──────────────────────────────────────────────

// Helper: refresh expired GitHub App user token
async Task<string?> RefreshGitHubTokenAsync(HttpContext ctx, IHttpClientFactory httpFactory)
{
    var refreshToken = ctx.Session.GetString("github_refresh_token");
    if (refreshToken is null) return null;

    var http = httpFactory.CreateClient();
    using var req = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token");
    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    req.Content = JsonContent.Create(new
    {
        client_id = gitHubClientId,
        client_secret = gitHubClientSecret,
        grant_type = "refresh_token",
        refresh_token = refreshToken
    });

    var res = await http.SendAsync(req);
    if (!res.IsSuccessStatusCode)
    {
        logger.LogWarning("GitHub token refresh failed: status={Status}", (int)res.StatusCode);
        tokenRefreshFailCounter.Add(1);
        return null;
    }

    var body = await res.Content.ReadAsStringAsync();
    var json = JsonSerializer.Deserialize<JsonElement>(body);
    if (json.TryGetProperty("error", out var refreshErr))
    {
        logger.LogWarning("GitHub token refresh error: {Error}", refreshErr);
        tokenRefreshFailCounter.Add(1);
        return null;
    }
    if (!json.TryGetProperty("access_token", out var newToken))
    {
        logger.LogWarning("GitHub token refresh: no access_token in response");
        tokenRefreshFailCounter.Add(1);
        return null;
    }

    var newAccessToken = newToken.GetString()!;
    ctx.Session.SetString("github_token", newAccessToken);
    if (json.TryGetProperty("expires_in", out var ghRefreshExpProp))
        ctx.Session.SetString("github_token_expiry", DateTimeOffset.UtcNow.AddSeconds(ghRefreshExpProp.GetInt32() - 300).ToString("o"));
    if (json.TryGetProperty("refresh_token", out var newRt))
        ctx.Session.SetString("github_refresh_token", newRt.GetString()!);

    logger.LogInformation("GitHub token refreshed successfully");
    tokenRefreshCounter.Add(1);
    return newAccessToken;
}

// ── Shared (stateless) AI tools — safe to share across all users ──
var sharedTools = new List<AIFunction>();
sharedTools.AddRange(ChartTools.Create());
sharedTools.AddRange(PricingTools.Create());
sharedTools.AddRange(HealthTools.Create());
sharedTools.AddRange(PresentationTools.Create());

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
        return tools;
    });
}

app.MapPost("/api/chat", (Delegate)(async (HttpContext ctx, IHttpClientFactory httpFactory) =>
{
    var githubToken = ctx.Session.GetString("github_token");
    var userJson = ctx.Session.GetString("user");

    // Proactively refresh GitHub token if expired (GitHub App user tokens expire after ~8h)
    if (githubToken is not null)
    {
        var ghExpiryStr = ctx.Session.GetString("github_token_expiry");
        if (ghExpiryStr is not null && DateTimeOffset.TryParse(ghExpiryStr, out var ghExpiry) && ghExpiry <= DateTimeOffset.UtcNow)
        {
            logger.LogInformation("GitHub token expired, attempting refresh");
            var refreshed = await RefreshGitHubTokenAsync(ctx, httpFactory);
            if (refreshed is not null)
                githubToken = refreshed;
        }
    }

    // If token is still missing, try one refresh attempt before returning 401
    if (githubToken is null)
    {
        var refreshed = await RefreshGitHubTokenAsync(ctx, httpFactory);
        if (refreshed is not null)
            githubToken = refreshed;
    }

    if (githubToken is null || userJson is null)
    {
        ctx.Response.StatusCode = 401;
        return;
    }

    using var bodyDoc = await JsonDocument.ParseAsync(ctx.Request.Body);
    var prompt = bodyDoc.RootElement.GetProperty("prompt").GetString();
    var model = bodyDoc.RootElement.TryGetProperty("model", out var m) ? m.GetString() : "claude-opus-4.6";

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
    chatRequestCounter.Add(1, new KeyValuePair<string, object?>("model", model), new KeyValuePair<string, object?>("user", userLogin));

    using var chatActivity = aiActivitySource.StartActivity("ChatRequest");
    chatActivity?.SetTag("ai.user", userLogin);
    chatActivity?.SetTag("ai.model", model);
    chatActivity?.SetTag("ai.prompt_length", prompt!.Length);
    chatActivity?.SetTag("ai.prompt", prompt.Length > 500 ? prompt[..500] + "..." : prompt);
    logger.LogInformation("Chat request from {User} model={Model} promptLen={PromptLen}", userLogin, model, prompt.Length);

    // Set per-request user context and update per-user token holder.
    // Use RefreshLock to serialize Azure token refresh for concurrent requests from the same user —
    // prevents double-refresh races when Azure rotates single-use refresh tokens.
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
        // Detect token change — if the user re-authenticated, we must recreate the client
        var previousToken = clientTokens.GetValueOrDefault(userId);
        var tokenChanged = previousToken is not null && previousToken != githubToken;

        if (tokenChanged)
        {
            logger.LogInformation("Token changed for user {User}, recreating CopilotClient", userLogin);
            chatActivity?.SetTag("ai.token_changed", true);
            if (clients.TryRemove(userId, out var old)) try { old.Dispose(); } catch { }
            if (sessions.TryRemove(userId, out _)) activeSessionsGauge.Add(-1);
        }

        var client = clients.GetOrAdd(userId,
            _ => new CopilotClient(new CopilotClientOptions { GitHubToken = githubToken }));
        clientTokens[userId] = githubToken;

        // Ensure started — if disconnected, recreate (stale process)
        if (client.State != ConnectionState.Connected)
        {
            if (clients.TryUpdate(userId, new CopilotClient(new CopilotClientOptions { GitHubToken = githubToken }), client))
            {
                try { client.Dispose(); } catch { }
                client = clients[userId];
            }
            await client.StartAsync();
        }

        // Get or create session — reuse across messages to preserve conversation history
        // Recreate session if model changed
        var currentModel = sessionModels.GetValueOrDefault(userId);
        if (currentModel is not null && currentModel != model)
        {
            sessions.TryRemove(userId, out _);
            sessionModels.TryRemove(userId, out _);
        }

        var sessionConfig = new SessionConfig
        {
            Model = model,
            Streaming = true,
            Tools = GetOrCreateUserTools(userId),
            OnPermissionRequest = (_, _) => Task.FromResult(new PermissionRequestResult { Kind = PermissionRequestResultKind.Approved }),
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = @"
You are the Azure FinOps Agent — a concise, data-driven AI assistant for Azure cost optimization.

## Rules
- Keep responses as short as possible. Minimize prose.
- Use a single wide table (many columns) instead of multiple tables or paragraphs. Pack all relevant info into one table.
- Max ONE chart per response.
- When discussing cost data, you understand the FOCUS (FinOps Open Cost & Usage Specification) standard: BilledCost, EffectiveCost, ListCost, ContractedCost, ListUnitPrice, ContractedUnitPrice, CommitmentDiscountId, ChargeCategory, etc. Map Cost Management output to FOCUS concepts when the user asks about FOCUS or standardized reporting.
- For unit economics, combine cost data with usage metrics (transactions, users, API calls) to calculate cost-per-unit KPIs.

## Tools
- **FetchUrl** — fetch Azure Retail Prices and other public HTTP URLs.
- **GetAzureServiceHealth** — get Azure service health incidents.
- **RenderChart / RenderAdvancedChart** — render ECharts visualizations.
- **GeneratePresentation** — generate a FinOps PowerPoint (.pptx) from structured slide data. Use when the user wants to export findings as a presentation. Suggest a FinOps-standard slide structure and ask the user to confirm before generating.
- **RunScript** — execute Python 3, bash, or SQLite code for data processing.
- **QueryAzure** — call any Azure ARM REST API using the signed-in user's token. Use for all Azure management queries (GET/POST only).
- **QueryGraph** — call Microsoft Graph API for license inventory, directory objects, org structure.
- **QueryLogAnalytics** — run KQL queries against Log Analytics workspaces or App Insights.

## Workflow
1. Use FetchUrl for public pricing data, QueryAzure for Azure tenant data.
2. Process results with RunScript if needed.
3. Visualize with RenderChart/RenderAdvancedChart.
4. Export to PowerPoint with GeneratePresentation when asked.
4. FetchUrl output starts with a 'Current UTC time: ...' line before the JSON. When parsing in RunScript, skip the first line.

## QueryAzure API Reference
If the user has not connected Azure, tell them to click 'Connect Azure' in the sidebar.
Base: https://management.azure.com (provide path starting with /)

Key APIs (method — path):
- GET /subscriptions?api-version=2022-12-01
- POST /{scope}/providers/Microsoft.CostManagement/query?api-version=2023-11-01 — cost analysis (body: {type:'ActualCost',timeframe:'MonthToDate',dataset:{granularity:'None',aggregation:{totalCost:{name:'Cost',function:'Sum'}},grouping:[{type:'Dimension',name:'ServiceName'}]}})
- POST /{scope}/providers/Microsoft.CostManagement/forecast?api-version=2023-11-01 — cost forecast
- GET /{scope}/providers/Microsoft.Consumption/budgets?api-version=2023-05-01
- GET /subscriptions/{subId}/providers/Microsoft.Advisor/recommendations?api-version=2023-01-01&$filter=Category eq 'Cost'
- POST /providers/Microsoft.ResourceGraph/resources?api-version=2022-10-01 — KQL queries (body: {query:'Resources | summarize count() by type',subscriptions:['sub-id']})
- GET /providers/Microsoft.Billing/billingAccounts?api-version=2024-04-01
- GET /providers/Microsoft.Capacity/reservationOrders?api-version=2022-11-01
- GET /providers/Microsoft.BillingBenefits/savingsPlanOrders?api-version=2022-11-01
- GET /{scope}/providers/Microsoft.CostManagement/benefitUtilizationSummaries?api-version=2023-11-01
- GET /{scope}/providers/Microsoft.CostManagement/benefitRecommendations?api-version=2023-11-01
- GET /subscriptions/{subId}/providers/Microsoft.Compute/skus?api-version=2021-07-01
- GET /{resourceId}/providers/Microsoft.Insights/metrics?api-version=2024-02-01
- GET /providers/Microsoft.Management/managementGroups?api-version=2021-04-01
- GET /subscriptions/{subId}/tagNames?api-version=2021-04-01
- GET /{scope}/providers/Microsoft.PolicyInsights/policyStates/latest/summarize?api-version=2024-10-01
- POST /{scope}/providers/Microsoft.CostManagement/generateCostDetailsReport?api-version=2023-11-01 — async line-item report
- GET /subscriptions/{subId}/providers/Microsoft.ResourceHealth/availabilityStatuses?api-version=2024-02-01
- POST /providers/Microsoft.Carbon/carbonEmissionReports?api-version=2023-04-01-preview — emissions data
- GET /subscriptions/{subId}/providers/Microsoft.MachineLearningServices/workspaces?api-version=2024-10-01 — Azure ML workspaces
- GET /subscriptions/{subId}/providers/Microsoft.MachineLearningServices/workspaces/{name}/computes?api-version=2024-10-01 — ML compute instances, clusters, GPU VMs
- GET /subscriptions/{subId}/providers/Microsoft.Databricks/workspaces?api-version=2024-05-01 — Databricks workspaces and pricing tier
- GET /subscriptions/{subId}/providers/Microsoft.Sql/managedInstances?api-version=2023-08-01 — SQL Managed Instances (vCore right-sizing)
- GET /subscriptions/{subId}/providers/Microsoft.DocumentDB/databaseAccounts?api-version=2024-05-15 — Cosmos DB accounts (RU/s optimization)
- GET /subscriptions/{subId}/providers/Microsoft.Cache/redis?api-version=2024-03-01 — Redis Cache (tier right-sizing)
- GET /subscriptions/{subId}/providers/Microsoft.Synapse/workspaces?api-version=2021-06-01 — Synapse workspaces and SQL/Spark pools
- GET /subscriptions/{subId}/providers/Microsoft.App/containerApps?api-version=2024-03-01 — Container Apps
Scope = /subscriptions/{subId} or /subscriptions/{subId}/resourceGroups/{rg}
"

            },
        };

        if (!sessions.TryGetValue(userId, out var session))
        {
            using var createSessionActivity = aiActivitySource.StartActivity("CreateCopilotSession");
            createSessionActivity?.SetTag("ai.user", userLogin);
            createSessionActivity?.SetTag("ai.model", model);
            session = await client.CreateSessionAsync(sessionConfig);
            sessions[userId] = session;
            sessionModels[userId] = model!;
            activeSessionsGauge.Add(1);
            sessionCreatedCounter.Add(1, new KeyValuePair<string, object?>("model", model), new KeyValuePair<string, object?>("user", userLogin));
            logger.LogInformation("Created new Copilot session for {User} model={Model} sessionId={SessionId}", userLogin, model, session.SessionId);
        }

        var done = new TaskCompletionSource();
        var cancelled = false;
        var authRetried = false;
        // Track tool calls: id → (name, startTime, activity)
        var toolTracker = new ConcurrentDictionary<string, (string Name, DateTimeOffset StartTime, Activity? Activity)>();

        ctx.RequestAborted.Register(async () =>
        {
            if (!cancelled)
            {
                cancelled = true;
                try { await session.AbortAsync(); } catch { }
                done.TrySetResult();
            }
        });

        // Pre-declare so the handler closure can capture them for auth retry
        IDisposable activeSubscription = null!;
        SessionEventHandler handleEvent = null!;

        // Event handler delegate — reused if session is recreated after expiry
        handleEvent = async (SessionEvent evt) =>
        {
            if (cancelled) return;

            try
            {
                string? sseData = null;

                if (evt is AssistantMessageDeltaEvent delta)
                {
                    sseData = JsonSerializer.Serialize(new { type = "delta", content = delta.Data.DeltaContent });
                }
                else if (evt is AssistantMessageEvent msg)
                {
                    sseData = JsonSerializer.Serialize(new { type = "message", content = msg.Data.Content });
                }
                else if (evt is ToolExecutionStartEvent toolStart)
                {
                    var toolId = toolStart.Data.ToolCallId ?? Guid.NewGuid().ToString();
                    toolCallCounter.Add(1, new KeyValuePair<string, object?>("tool", toolStart.Data.ToolName), new KeyValuePair<string, object?>("user", userLogin));
                    var toolActivity = aiActivitySource.StartActivity($"Tool:{toolStart.Data.ToolName}");
                    toolActivity?.SetTag("ai.tool.name", toolStart.Data.ToolName);
                    toolActivity?.SetTag("ai.tool.id", toolId);
                    toolTracker[toolId] = (toolStart.Data.ToolName, DateTimeOffset.UtcNow, toolActivity);
                    string? argsJson = null;
                    if (toolStart.Data.Arguments is not null)
                    {
                        try { argsJson = JsonSerializer.Serialize(toolStart.Data.Arguments); } catch { }
                    }
                    toolActivity?.SetTag("ai.tool.args", argsJson?.Length > 1000 ? argsJson[..1000] + "..." : argsJson);
                    logger.LogInformation("Tool start: {Tool} id={ToolId} args={Args}", toolStart.Data.ToolName, toolId, argsJson?.Length > 500 ? argsJson[..500] + "..." : argsJson);
                    sseData = JsonSerializer.Serialize(new { type = "tool_start", tool = toolStart.Data.ToolName, id = toolId, args = argsJson });
                }
                else if (evt is ToolExecutionCompleteEvent toolDone)
                {
                    var toolId = toolDone.Data.ToolCallId ?? "";
                    var toolName = toolTracker.TryGetValue(toolId, out var info) ? info.Name : "unknown";
                    var durationMs = toolTracker.TryGetValue(toolId, out var info2) ? (long)(DateTimeOffset.UtcNow - info2.StartTime).TotalMilliseconds : (long?)null;
                    // Complete the tool activity span
                    if (toolTracker.TryRemove(toolId, out var removed))
                    {
                        removed.Activity?.SetTag("ai.tool.success", toolDone.Data.Success);
                        removed.Activity?.SetTag("ai.tool.durationMs", durationMs);
                        if (toolDone.Data.Error?.Message is not null)
                            removed.Activity?.SetTag("ai.tool.error", toolDone.Data.Error.Message);
                        removed.Activity?.Dispose();
                    }
                    string? resultText = null;
                    string? errorText = null;
                    if (toolDone.Data.Result?.Content is not null)
                        resultText = toolDone.Data.Result.Content;
                    else if (toolDone.Data.Result?.DetailedContent is not null)
                        resultText = toolDone.Data.Result.DetailedContent;
                    if (toolDone.Data.Error?.Message is not null)
                        errorText = toolDone.Data.Error.Message;
                    if (!toolDone.Data.Success)
                        toolErrorCounter.Add(1, new KeyValuePair<string, object?>("tool", toolName), new KeyValuePair<string, object?>("user", userLogin));
                    sseData = JsonSerializer.Serialize(new { type = "tool_done", tool = toolName, id = toolId, success = toolDone.Data.Success, durationMs, result = resultText, error = errorText });
                    logger.LogInformation("Tool done: {Tool} id={ToolId} success={Success} durationMs={Duration} resultLen={ResultLen}",
                        toolName, toolId, toolDone.Data.Success, durationMs, resultText?.Length ?? 0);
                    // Log short results or errors in full for diagnostics (e.g. GeneratePresentation failures)
                    if (resultText is not null && (resultText.Length < 200 || resultText.StartsWith("Error")))
                        logger.LogInformation("Tool result [{Tool}]: {Result}", toolName, resultText);
                    if (errorText is not null)
                        logger.LogWarning("Tool error [{Tool}]: {Error}", toolName, errorText);

                    // If this is a RenderChart or RenderAdvancedChart tool completion, also emit a chart event
                    // Also detect __CHART__: marker in any tool output for inline chart rendering
                    if ((toolName == "RenderChart" || toolName == "RenderAdvancedChart") && toolDone.Data.Success && resultText is not null)
                    {
                        try
                        {
                            await ctx.Response.WriteAsync($"data: {sseData}\n\n");
                            await ctx.Response.Body.FlushAsync();
                            var chartData = JsonSerializer.Serialize(new { type = "chart", options = resultText });
                            await ctx.Response.WriteAsync($"data: {chartData}\n\n");
                            await ctx.Response.Body.FlushAsync();
                            sseData = null;
                        }
                        catch { }
                    }
                    else if (toolDone.Data.Success && resultText is not null && resultText.Contains("__CHART__:"))
                    {
                        try
                        {
                            // Extract chart JSON from __CHART__: lines in output
                            foreach (var line in resultText.Split('\n'))
                            {
                                var trimmed = line.Trim();
                                if (trimmed.StartsWith("__CHART__:"))
                                {
                                    var chartJson = trimmed["__CHART__:".Length..].Trim();
                                    // Emit tool_done first, then chart event
                                    await ctx.Response.WriteAsync($"data: {sseData}\n\n");
                                    await ctx.Response.Body.FlushAsync();
                                    var chartPayload = JsonSerializer.Serialize(new { type = "chart", options = chartJson });
                                    await ctx.Response.WriteAsync($"data: {chartPayload}\n\n");
                                    await ctx.Response.Body.FlushAsync();
                                    sseData = null;
                                    break; // Only one chart per response
                                }
                            }
                        }
                        catch { }
                    }
                    // Detect __PPTX_READY__ marker in GeneratePresentation tool output
                    if (toolDone.Data.Success && resultText is not null && resultText.Contains("__PPTX_READY__:"))
                    {
                        try
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
                                        if (sseData is not null)
                                        {
                                            await ctx.Response.WriteAsync($"data: {sseData}\n\n");
                                            await ctx.Response.Body.FlushAsync();
                                        }
                                        await ctx.Response.WriteAsync($"data: {pptxPayload}\n\n");
                                        await ctx.Response.Body.FlushAsync();
                                        sseData = null;
                                    }
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
                }
                else if (evt is SessionErrorEvent error)
                {
                    // Retry once on authorization errors — fresh GitHub tokens may not be
                    // immediately recognized by the Copilot backend (propagation delay).
                    if (!authRetried && error.Data.Message.Contains("Authorization error", StringComparison.OrdinalIgnoreCase))
                    {
                        authRetried = true;
                        logger.LogWarning("Authorization error for {User}, retrying with fresh client. Error: {Error}", userLogin, error.Data.Message);
                        chatActivity?.SetTag("ai.auth_retry", true);

                        // Tear down stale client + session
                        if (clients.TryRemove(userId, out var oldClient)) try { oldClient.Dispose(); } catch { }
                        if (sessions.TryRemove(userId, out _)) activeSessionsGauge.Add(-1);
                        clientTokens.TryRemove(userId, out _);

                        try
                        {
                            // Create fresh client + session
                            var freshClient = new CopilotClient(new CopilotClientOptions { GitHubToken = githubToken });
                            clients[userId] = freshClient;
                            clientTokens[userId] = githubToken;
                            await freshClient.StartAsync();

                            var freshSession = await freshClient.CreateSessionAsync(sessionConfig);
                            sessions[userId] = freshSession;
                            sessionModels[userId] = model!;
                            activeSessionsGauge.Add(1);
                            sessionCreatedCounter.Add(1, new KeyValuePair<string, object?>("model", model), new KeyValuePair<string, object?>("user", userLogin));
                            logger.LogInformation("Auth retry: recreated client+session for {User} sessionId={SessionId}", userLogin, freshSession.SessionId);

                            // Switch subscription to new session
                            activeSubscription.Dispose();
                            activeSubscription = freshSession.On(handleEvent);

                            // Resend — new session events will flow through this same handler
                            await freshSession.SendAsync(new MessageOptions { Prompt = prompt });
                            return;
                        }
                        catch (Exception retryEx)
                        {
                            logger.LogError(retryEx, "Auth retry failed for {User}", userLogin);
                            // Fall through to normal error handling
                        }
                    }

                    sseData = JsonSerializer.Serialize(new { type = "error", message = error.Data.Message });
                    logger.LogError("Session error for {User}: {Error}", userLogin, error.Data.Message);
                    chatActivity?.SetTag("ai.error", error.Data.Message);
                    sessions.TryRemove(userId, out _);
                    clients.TryRemove(userId, out _);
                }

                if (sseData is not null)
                {
                    await ctx.Response.WriteAsync($"data: {sseData}\n\n");
                    await ctx.Response.Body.FlushAsync();
                }

                if (evt is SessionIdleEvent || evt is SessionErrorEvent)
                {
                    await ctx.Response.WriteAsync("data: [DONE]\n\n");
                    await ctx.Response.Body.FlushAsync();
                    done.TrySetResult();
                }
            }
            catch
            {
                cancelled = true;
                done.TrySetResult();
            }
        };

        // Subscribe to session events
        activeSubscription = session.On(handleEvent);

        try
        {
            await session.SendAsync(new MessageOptions { Prompt = prompt });
        }
        catch (Exception sendEx) when (sendEx.Message.Contains("Session not found", StringComparison.OrdinalIgnoreCase))
        {
            // Session expired on the Copilot CLI side (e.g. after prolonged inactivity).
            // Remove stale session, create a fresh one, and retry transparently.
            logger.LogWarning("Copilot session expired for user {User}, recreating session. Error: {Error}", userLogin, sendEx.Message);
            chatActivity?.SetTag("ai.session_expired", true);
            sessionExpiredCounter.Add(1, new KeyValuePair<string, object?>("user", userLogin), new KeyValuePair<string, object?>("model", model));
            activeSubscription.Dispose();
            if (sessions.TryRemove(userId, out _)) activeSessionsGauge.Add(-1);
            sessionModels.TryRemove(userId, out _);

            session = await client.CreateSessionAsync(sessionConfig);
            sessions[userId] = session;
            sessionModels[userId] = model!;
            activeSessionsGauge.Add(1);
            sessionCreatedCounter.Add(1, new KeyValuePair<string, object?>("model", model), new KeyValuePair<string, object?>("user", userLogin));
            logger.LogInformation("Recreated Copilot session for {User} model={Model} sessionId={SessionId}", userLogin, model, session.SessionId);

            activeSubscription = session.On(handleEvent);
            await session.SendAsync(new MessageOptions { Prompt = prompt });
        }

        try { await done.Task; }
        finally
        {
            activeSubscription.Dispose();
            chatSw.Stop();
            chatDurationHistogram.Record(chatSw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("model", model), new KeyValuePair<string, object?>("user", userLogin));
            chatActivity?.SetTag("ai.duration_ms", chatSw.Elapsed.TotalMilliseconds);
        }
    }
    catch (Exception ex)
    {
        chatSw.Stop();
        chatErrorCounter.Add(1, new KeyValuePair<string, object?>("model", model), new KeyValuePair<string, object?>("user", userLogin), new KeyValuePair<string, object?>("error_type", ex.GetType().Name));
        chatActivity?.SetTag("ai.error", ex.Message);
        chatActivity?.SetTag("ai.error_type", ex.GetType().Name);
        logger.LogError(ex, "Chat request failed for {User} model={Model}", userLogin, model);
        if (sessions.TryRemove(userId, out _)) activeSessionsGauge.Add(-1);
        clients.TryRemove(userId, out _);
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

    // Delete session permanently (wipes conversation history from disk)
    // DisposeAsync() alone only releases in-memory resources — disk data persists.
    if (sessions.TryRemove(userId, out var oldSession))
    {
        activeSessionsGauge.Add(-1);
        try
        {
            var sessionId = oldSession.SessionId;
            logger.LogInformation("Resetting session {SessionId} for user {UserId}", sessionId, userId);
            await oldSession.DisposeAsync();
            // Client must stay alive to call DeleteSessionAsync
            if (clients.TryGetValue(userId, out var client) && client.State == ConnectionState.Connected)
                await client.DeleteSessionAsync(sessionId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error deleting Copilot session for user {UserId}", userId);
        }
    }
    sessionModels.TryRemove(userId, out _);

    ctx.Response.StatusCode = 204;
});

// ──────────────────────────────────────────────
// MODELS ENDPOINT
// ──────────────────────────────────────────────

app.MapGet("/api/models", async (HttpContext ctx, IHttpClientFactory httpFactory) =>
{
    var githubToken = ctx.Session.GetString("github_token");

    // Proactively refresh GitHub token if expired
    if (githubToken is not null)
    {
        var ghExpiryStr = ctx.Session.GetString("github_token_expiry");
        if (ghExpiryStr is not null && DateTimeOffset.TryParse(ghExpiryStr, out var ghExpiry) && ghExpiry <= DateTimeOffset.UtcNow)
        {
            var refreshed = await RefreshGitHubTokenAsync(ctx, httpFactory);
            if (refreshed is not null)
                githubToken = refreshed;
        }
    }

    if (githubToken is null)
    {
        var refreshed = await RefreshGitHubTokenAsync(ctx, httpFactory);
        if (refreshed is not null)
            githubToken = refreshed;
        else
        {
            ctx.Response.StatusCode = 401;
            return;
        }
    }

    var userJson = ctx.Session.GetString("user");
    if (userJson is null)
    {
        ctx.Response.StatusCode = 401;
        return;
    }

    var user = JsonSerializer.Deserialize<JsonElement>(userJson);
    var userId = user.GetProperty("id").GetInt64();

    try
    {
        var client = clients.GetOrAdd(userId,
            _ => new CopilotClient(new CopilotClientOptions { GitHubToken = githubToken }));
        clientTokens[userId] = githubToken;

        if (client.State != ConnectionState.Connected)
        {
            if (clients.TryUpdate(userId, new CopilotClient(new CopilotClientOptions { GitHubToken = githubToken }), client))
            {
                try { client.Dispose(); } catch { }
                client = clients[userId];
            }
            await client.StartAsync();
        }

        var models = await client.ListModelsAsync();
        await ctx.Response.WriteAsJsonAsync(models);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Models endpoint failed for user {UserId}", userId);
        ctx.Response.StatusCode = 500;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
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
