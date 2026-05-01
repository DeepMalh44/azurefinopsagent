using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using GitHub.Copilot.SDK;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.AI;
using AzureFinOps.Dashboard.Auth;
using AzureFinOps.Dashboard.AI.Tools;
using AzureFinOps.Dashboard.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Load local settings only in Development
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// Data protection for cookie encryption — persist keys so sessions survive container restarts
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(
        Path.Combine(builder.Environment.IsDevelopment() ? Path.GetTempPath() : "/home", "dataprotection-keys")))
    .SetApplicationName("AzureFinOpsAgent");

// Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(24);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
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

// Azure OpenAI configuration (for BYOK bearer token)
var azureOpenAIEndpoint = app.Configuration["AzureOpenAI:Endpoint"] ?? "https://finops-agent-ai.openai.azure.com/";
var azureOpenAIDeployment = app.Configuration["AzureOpenAI:DeploymentName"] ?? "gpt-5.4";

// Entra ID credential for Azure OpenAI BYOK — generates short-lived bearer tokens
var azureOpenAICredential = new ClientSecretCredential(msHomeTenantId, msClientId, msClientSecret);
var cognitiveServicesScope = new Azure.Core.TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" });

// Cache the bearer token (refresh when expired — ~1 hour lifetime)
string? cachedBearerToken = null;
DateTimeOffset bearerTokenExpiry = DateTimeOffset.MinValue;
var bearerTokenLock = new SemaphoreSlim(1, 1);

async Task<string> GetAzureOpenAIBearerTokenAsync()
{
    if (cachedBearerToken is not null && bearerTokenExpiry > DateTimeOffset.UtcNow.AddMinutes(5))
        return cachedBearerToken;

    await bearerTokenLock.WaitAsync();
    try
    {
        // Double-check after acquiring lock
        if (cachedBearerToken is not null && bearerTokenExpiry > DateTimeOffset.UtcNow.AddMinutes(5))
            return cachedBearerToken;

        var tokenResult = await azureOpenAICredential.GetTokenAsync(cognitiveServicesScope, CancellationToken.None);
        cachedBearerToken = tokenResult.Token;
        bearerTokenExpiry = tokenResult.ExpiresOn;
        logger.LogInformation("Azure OpenAI bearer token refreshed, expires at {Expiry}", bearerTokenExpiry);
        return cachedBearerToken;
    }
    finally
    {
        bearerTokenLock.Release();
    }
}

logger.LogInformation("Azure OpenAI BYOK configured: endpoint={Endpoint} deployment={Deployment}", azureOpenAIEndpoint, azureOpenAIDeployment);

// Shared CopilotClient — manages the CLI process lifecycle (no GitHub auth needed with BYOK)
var copilotClient = new CopilotClient();
await copilotClient.StartAsync();
logger.LogInformation("CopilotClient started");

// Per-user CopilotSession caches
var userSessions = new ConcurrentDictionary<long, CopilotSession>();
var userTokens = new ConcurrentDictionary<long, UserTokens>();
var userTools = new ConcurrentDictionary<long, List<AIFunction>>();

// OAuth tier → resource-specific scopes (single source of truth for both redirect and token exchange)
string[] GetScopesForTier(string tier) => tier switch
{
    "licenses" => ["https://graph.microsoft.com/User.Read", "https://graph.microsoft.com/Organization.Read.All", "https://graph.microsoft.com/Reports.Read.All"],
    "chargeback" => ["https://graph.microsoft.com/User.Read", "https://graph.microsoft.com/User.Read.All", "https://graph.microsoft.com/Group.Read.All"],
    "loganalytics" => ["https://api.loganalytics.io/Data.Read"],
    "storage" => ["https://storage.azure.com/user_impersonation"],
    _ => ["https://management.azure.com/user_impersonation"]
};

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
app.MapPost("/auth/logout", async (HttpContext ctx) =>
{
    var userJson = ctx.Session.GetString("user");
    if (userJson is not null)
    {
        var u = JsonSerializer.Deserialize<JsonElement>(userJson);
        var uid = u.GetProperty("id").GetInt64();
        if (userSessions.TryRemove(uid, out var oldSession))
        {
            activeSessionsGauge.Add(-1);
            try { await oldSession.DisposeAsync(); } catch { }
        }
        userTokens.TryRemove(uid, out _);
        userTools.TryRemove(uid, out _);
    }
    ctx.Session.Clear();
    return Results.Ok(new { ok = true });
});

// ──────────────────────────────────────────────
// AZURE / MICROSOFT ENTRA ID OAUTH (Multi-Tenant)
// ──────────────────────────────────────────────

// Redirect to Microsoft Entra ID login.
// Supports incremental consent via ?tier= parameter — each tier adds only the
// scopes it needs, so the admin sees a minimal, justifiable consent screen:
//   base (default) — Azure ARM only (Cost Management, Billing, Advisor, etc.)
//   licenses       — M365 license inventory + usage reports (find unused seats)
//   chargeback     — user profiles + groups (department-based cost allocation)
//   loganalytics   — Log Analytics/App Insights KQL (VM metrics, ingestion costs)
app.MapGet("/auth/microsoft", (HttpContext ctx) =>
{
    if (string.IsNullOrEmpty(msClientId))
        return Results.Problem("Microsoft OAuth is not configured");

    var state = Guid.NewGuid().ToString("N");
    ctx.Session.SetString("ms_oauth_state", state);

    var tier = ctx.Request.Query["tier"].ToString().ToLowerInvariant();
    if (string.IsNullOrEmpty(tier)) tier = "base";

    // After admin consent succeeds, the frontend redirects here with tier=licenses.
    // Silent-chain through every remaining add-on tier so the user's session
    // accumulates all four tokens in one go (no consent screens — admin already
    // pre-approved). Triggered only when ?postadmin=1 is present.
    if (ctx.Request.Query["postadmin"].ToString() == "1")
    {
        var chain = new List<string> { "chargeback", "loganalytics", "storage" };
        ctx.Session.SetString("auth_chain", string.Join(",", chain));
        ctx.Session.SetString("auth_silent", "1");
    }

    ctx.Session.SetString("auth_tier", tier);

    // Allow user to specify a tenant (GUID or domain) — useful for guest users
    // who belong to multiple tenants and want to sign into a non-home tenant.
    var tenantParam = ctx.Request.Query["tenant"].ToString().Trim();
    if (!string.IsNullOrEmpty(tenantParam))
        ctx.Session.SetString("auth_tenant", tenantParam);
    var effectiveTenant = ctx.Session.GetString("auth_tenant") ?? msTenantId;

    var redirectUri = $"{NormalizeCallbackHost(ctx)}/auth/microsoft/callback";

    // Build scopes: base OIDC + tier-specific resource scopes
    var scope = string.Join(" ", ["openid", "profile", "email", "offline_access", .. GetScopesForTier(tier)]);
    // Base tier: select_account unless force_consent was set (from revoke)
    // Add-on tiers: 'consent' to show new permissions, unless we're in the
    // silent post-admin chain (prompt=none means "reuse existing session, no UI").
    var forceConsent = ctx.Session.GetString("force_consent") == "1";
    ctx.Session.Remove("force_consent");
    var silentChain = ctx.Session.GetString("auth_silent") == "1";
    string promptType;
    if (silentChain)
        promptType = "none";
    else if (tier != "base" || forceConsent)
        promptType = "consent";
    else
        promptType = "select_account";

    var url = $"https://login.microsoftonline.com/{Uri.EscapeDataString(effectiveTenant)}/oauth2/v2.0/authorize" +
              $"?client_id={Uri.EscapeDataString(msClientId)}" +
              $"&response_type=code" +
              $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
              $"&scope={Uri.EscapeDataString(scope)}" +
              $"&state={state}" +
              $"&response_mode=query" +
              $"&prompt={promptType}";

    logger.LogInformation("Microsoft OAuth redirect: tier={Tier} prompt={Prompt} tenant={Tenant} from {Host}", tier, promptType, effectiveTenant, ctx.Request.Host);
    return Results.Redirect(url);
});

// Tenant-wide admin consent — one click grants every required permission across
// all four resources (ARM, Microsoft Graph, Log Analytics, Azure Storage) for
// every user in the tenant. Only Global Admins / Privileged Role Admins can
// approve. Non-admin users will see "You need admin approval" from Entra and
// should fall back to per-scope individual buttons.
app.MapGet("/auth/microsoft/adminconsent", (HttpContext ctx) =>
{
    if (string.IsNullOrEmpty(msClientId))
        return Results.Problem("Microsoft OAuth is not configured");

    var state = Guid.NewGuid().ToString("N");
    ctx.Session.SetString("ms_oauth_state", state);
    ctx.Session.SetString("auth_tier", "adminconsent");

    // Allow user to specify a tenant — admin consent must target a specific tenant
    // (cannot use /common). Default to user's home tenant if previously known.
    var tenantParam = ctx.Request.Query["tenant"].ToString().Trim();
    if (!string.IsNullOrEmpty(tenantParam))
        ctx.Session.SetString("auth_tenant", tenantParam);
    var effectiveTenant = ctx.Session.GetString("auth_tenant") ?? msTenantId;
    // /common does not work for /adminconsent — fall back to /organizations
    // which lets the admin pick which tenant to consent for.
    if (effectiveTenant == "common") effectiveTenant = "organizations";

    var redirectUri = $"{NormalizeCallbackHost(ctx)}/auth/microsoft/adminconsent/callback";

    var url = $"https://login.microsoftonline.com/{Uri.EscapeDataString(effectiveTenant)}/v2.0/adminconsent" +
              $"?client_id={Uri.EscapeDataString(msClientId)}" +
              $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
              $"&state={state}" +
              $"&scope=https://graph.microsoft.com/.default";

    logger.LogInformation("Admin consent redirect: tenant={Tenant} from {Host}", effectiveTenant, ctx.Request.Host);
    return Results.Redirect(url);
});

// Admin consent callback — Entra returns ?admin_consent=True&tenant={id} on
// success or ?error=...&error_description=... on failure. No tokens; users
// must still individually sign in via /auth/microsoft to get their own tokens
// (which will now be silent — no consent screen — because admin pre-approved).
app.MapGet("/auth/microsoft/adminconsent/callback", (HttpContext ctx) =>
{
    var state = ctx.Request.Query["state"].ToString();
    if (state != ctx.Session.GetString("ms_oauth_state"))
    {
        logger.LogWarning("Admin consent state mismatch — possible CSRF");
        return Results.StatusCode(403);
    }
    ctx.Session.Remove("ms_oauth_state");

    var error = ctx.Request.Query["error"].ToString();
    if (!string.IsNullOrEmpty(error))
    {
        var desc = ctx.Request.Query["error_description"].ToString();
        logger.LogWarning("Admin consent failed: {Error} — {Desc}", error, desc);
        return Results.Redirect("/?azure_error=" + Uri.EscapeDataString(error));
    }

    var grantedTenant = ctx.Request.Query["tenant"].ToString();
    logger.LogInformation("Admin consent granted for tenant={Tenant}", grantedTenant);
    return Results.Redirect("/?admin_consent=ok&tenant=" + Uri.EscapeDataString(grantedTenant));
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

        // Use the same tenant the user selected during the authorize step
        var effectiveTenant = ctx.Session.GetString("auth_tenant") ?? msTenantId;

        using var tokenReq = new HttpRequestMessage(HttpMethod.Post,
            $"https://login.microsoftonline.com/{Uri.EscapeDataString(effectiveTenant)}/oauth2/v2.0/token");

        // Token exchange scope must match the tier that was requested
        var authTier = ctx.Session.GetString("auth_tier") ?? "base";
        var tokenExchangeScope = string.Join(" ", ["openid", "profile", "email", "offline_access", .. GetScopesForTier(authTier)]);

        tokenReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = msClientId,
            ["client_secret"] = msClientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["scope"] = tokenExchangeScope
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

        var accessToken = atProp.GetString()!;
        var refreshToken = tokenJson.TryGetProperty("refresh_token", out var rtProp) ? rtProp.GetString() : null;
        var expiresIn = tokenJson.TryGetProperty("expires_in", out var expProp) ? expProp.GetInt32() : 3600;

        // Store token based on the tier — the code exchange already returns the right resource token
        if (authTier == "licenses" || authTier == "chargeback")
        {
            // Graph token — store directly from code exchange
            ctx.Session.SetString("graph_token", accessToken);
            ctx.Session.SetString("graph_token_expiry", DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60).ToString("o"));
            var existingTier = ctx.Session.GetString("graph_tier") ?? "";
            if (!existingTier.Contains(authTier))
                ctx.Session.SetString("graph_tier", string.IsNullOrEmpty(existingTier) ? authTier : $"{existingTier},{authTier}");
        }
        else if (authTier == "loganalytics")
        {
            // Log Analytics token — store directly
            ctx.Session.SetString("loganalytics_token", accessToken);
            ctx.Session.SetString("loganalytics_token_expiry", DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60).ToString("o"));
        }
        else if (authTier == "storage")
        {
            // Azure Storage data-plane token — for reading cost exports
            ctx.Session.SetString("storage_token", accessToken);
            ctx.Session.SetString("storage_token_expiry", DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60).ToString("o"));
        }
        else
        {
            // Base tier — ARM token
            ctx.Session.SetString("azure_token", accessToken);
            ctx.Session.SetString("azure_token_expiry", DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60).ToString("o"));
        }

        // Always store refresh token (useful for token refresh later)
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

        logger.LogInformation("Microsoft OAuth login successful, tier={Tier}", authTier);

        // If a chain is pending (post-admin-consent silent acquisition), continue with the next tier.
        var pendingChain = ctx.Session.GetString("auth_chain");
        if (!string.IsNullOrEmpty(pendingChain))
        {
            var parts = pendingChain.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                var next = parts[0];
                var rest = string.Join(",", parts.Skip(1));
                ctx.Session.SetString("auth_chain", rest);
                return Results.Redirect($"/auth/microsoft?tier={Uri.EscapeDataString(next)}");
            }
            ctx.Session.Remove("auth_chain");
        }

        // Clear silent-chain flag once the chain (or single tier) is fully done.
        ctx.Session.Remove("auth_silent");

        return Results.Redirect("/");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Microsoft OAuth callback failed");
        return Results.Redirect("/?azure_error=callback_failed");
    }
});

// Helper: exchange refresh token for a token scoped to a specific resource
async Task<(string Token, DateTimeOffset Expiry)?> ExchangeRefreshTokenForResource(HttpClient http, string refreshToken, string scope, string? tenantOverride = null)
{
    var effectiveTenant = tenantOverride ?? msTenantId;
    using var req = new HttpRequestMessage(HttpMethod.Post,
        $"https://login.microsoftonline.com/{Uri.EscapeDataString(effectiveTenant)}/oauth2/v2.0/token");
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

// Per-(session,tokenKey) refresh locks — prevents concurrent SSE/tool calls from
// double-refreshing the same token. Locks are keyed by sessionId|tokenKey.
var refreshLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

// Helper: get valid token from session, auto-refresh if expired
async Task<string?> GetSessionTokenAsync(HttpContext ctx, IHttpClientFactory httpFactory,
    string tokenKey, string expiryKey, string refreshScope)
{
    var token = ctx.Session.GetString(tokenKey);
    if (token is null) return null;

    var expiryStr = ctx.Session.GetString(expiryKey);
    if (expiryStr is null || !DateTimeOffset.TryParse(expiryStr, out var expiry) || expiry > DateTimeOffset.UtcNow)
        return token; // still valid

    // Token expired — serialize refresh per session+token to avoid duplicate refreshes
    var lockKey = $"{ctx.Session.Id}|{tokenKey}";
    var sem = refreshLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
    await sem.WaitAsync(ctx.RequestAborted);
    try
    {
        // Double-check: another request may have refreshed while we were waiting
        var freshToken = ctx.Session.GetString(tokenKey);
        var freshExpiryStr = ctx.Session.GetString(expiryKey);
        if (freshToken is not null && freshExpiryStr is not null
            && DateTimeOffset.TryParse(freshExpiryStr, out var freshExpiry)
            && freshExpiry > DateTimeOffset.UtcNow)
        {
            return freshToken;
        }

        var refreshToken = ctx.Session.GetString("azure_refresh_token");
        if (refreshToken is null)
        {
            logger.LogWarning("Token {Key} expired but no refresh token available, returning expired token", tokenKey);
            return token;
        }
        var http = httpFactory.CreateClient();
        var sessionTenant = ctx.Session.GetString("auth_tenant");
        var result = await ExchangeRefreshTokenForResource(http, refreshToken, refreshScope, sessionTenant);
        if (result is null)
        {
            logger.LogWarning("Token refresh failed for {Key}, returning expired token as fallback", tokenKey);
            return token;
        }
        ctx.Session.SetString(tokenKey, result.Value.Token);
        ctx.Session.SetString(expiryKey, result.Value.Expiry.ToString("o"));
        return result.Value.Token;
    }
    finally
    {
        sem.Release();
    }
}

Task<string?> GetAzureTokenAsync(HttpContext ctx, IHttpClientFactory httpFactory) =>
    GetSessionTokenAsync(ctx, httpFactory, "azure_token", "azure_token_expiry",
        "openid profile email https://management.azure.com/user_impersonation offline_access");

Task<string?> GetGraphTokenAsync(HttpContext ctx, IHttpClientFactory httpFactory) =>
    GetSessionTokenAsync(ctx, httpFactory, "graph_token", "graph_token_expiry",
        "https://graph.microsoft.com/User.Read https://graph.microsoft.com/User.Read.All https://graph.microsoft.com/Organization.Read.All https://graph.microsoft.com/Group.Read.All https://graph.microsoft.com/Reports.Read.All offline_access");

Task<string?> GetLogAnalyticsTokenAsync(HttpContext ctx, IHttpClientFactory httpFactory) =>
    GetSessionTokenAsync(ctx, httpFactory, "loganalytics_token", "loganalytics_token_expiry",
        "https://api.loganalytics.io/Data.Read offline_access");

Task<string?> GetStorageTokenAsync(HttpContext ctx, IHttpClientFactory httpFactory) =>
    GetSessionTokenAsync(ctx, httpFactory, "storage_token", "storage_token_expiry",
        "https://storage.azure.com/user_impersonation offline_access");

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
                    state = sub.GetProperty("state").GetString(),
                    tenantId = sub.TryGetProperty("tenantId", out var tid) ? tid.GetString() : null
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
        apis = connectedApis,
        graphEnabled = ctx.Session.GetString("graph_token") is not null,
        graphTier = ctx.Session.GetString("graph_tier") ?? "",
        logAnalyticsEnabled = ctx.Session.GetString("loganalytics_token") is not null,
        storageEnabled = ctx.Session.GetString("storage_token") is not null
    });
});

// List all tenants the user has access to (for tenant switcher)
app.MapGet("/auth/azure/tenants", async (HttpContext ctx, IHttpClientFactory httpFactory) =>
{
    var token = await GetAzureTokenAsync(ctx, httpFactory);
    if (token is null)
        return Results.Json(new { tenants = Array.Empty<object>() });

    var http = httpFactory.CreateClient();
    var tenants = new List<object>();
    try
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://management.azure.com/tenants?api-version=2022-12-01");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("User-Agent", "FinOps-Dashboard/1.0");
        var res = await http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        var json = JsonSerializer.Deserialize<JsonElement>(body);
        if (json.TryGetProperty("value", out var vals))
        {
            foreach (var t in vals.EnumerateArray())
            {
                tenants.Add(new
                {
                    tenantId = t.GetProperty("tenantId").GetString(),
                    displayName = t.TryGetProperty("displayName", out var dn) ? dn.GetString() : null,
                    defaultDomain = t.TryGetProperty("defaultDomain", out var dd) ? dd.GetString() : null
                });
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to list tenants");
    }

    // Include current tenant from the ID token so the UI can highlight it
    var currentTenantId = "";
    var azureUserJson = ctx.Session.GetString("azure_user");
    if (azureUserJson is not null)
    {
        var u = JsonSerializer.Deserialize<JsonElement>(azureUserJson);
        if (u.TryGetProperty("tenantId", out var tid))
            currentTenantId = tid.GetString() ?? "";
    }

    return Results.Json(new { tenants, currentTenantId });
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
            tokens.StorageToken = null;
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
    ctx.Session.Remove("graph_tier");
    ctx.Session.Remove("loganalytics_token");
    ctx.Session.Remove("loganalytics_token_expiry");
    ctx.Session.Remove("storage_token");
    ctx.Session.Remove("storage_token_expiry");
    ctx.Session.Remove("auth_tenant");
    return Results.Ok(new { ok = true });
});

// Revoke all Entra ID permissions — deletes the user's consent grants for this app
// Revoke: clear session and set a flag to force consent on next connect.
// Note: Actually revoking Entra ID consent grants requires admin-level Graph permissions
// that we don't request. Instead, we force prompt=consent on the next connect so the
// consent screen always appears, and we direct the user to myapps.microsoft.com to
// fully revoke at the Entra ID level if needed.
app.MapPost("/auth/azure/revoke", (HttpContext ctx) =>
{
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
            tokens.StorageToken = null;
        }
        userSessions.TryRemove(uid, out _);
        userTools.TryRemove(uid, out _);
    }

    ctx.Session.Remove("azure_token");
    ctx.Session.Remove("azure_refresh_token");
    ctx.Session.Remove("azure_token_expiry");
    ctx.Session.Remove("azure_user");
    ctx.Session.Remove("graph_token");
    ctx.Session.Remove("graph_token_expiry");
    ctx.Session.Remove("graph_tier");
    ctx.Session.Remove("loganalytics_token");
    ctx.Session.Remove("loganalytics_token_expiry");
    ctx.Session.Remove("storage_token");
    ctx.Session.Remove("storage_token_expiry");
    // Flag to force consent screen on next connect
    ctx.Session.SetString("force_consent", "1");
    logger.LogInformation("Azure revoked — session cleared, next connect will force consent");
    return Results.Ok(new { ok = true });
});

// ──────────────────────────────────────────────
// CHAT SSE ENDPOINT (Copilot SDK with Azure OpenAI BYOK)
// ──────────────────────────────────────────────

// System prompt for the FinOps Agent
const string SystemPrompt = @"
You are the Azure FinOps Agent — a data-driven AI assistant for Azure cloud cost optimization and InfraOps.

## Rules
- Keep answers as short as possible. Lead with a 1-2 sentence summary.
- Do NOT output thinking or progress text like '*Querying...*' — the UI shows tool progress separately. Only output the final answer.
- The user's Azure connection status is injected at the start of each message. Trust that status. NEVER proactively suggest connecting Azure unless a tool call returns an authentication/token error.
- Choose EITHER a chart OR a table per response — never both. Chart for visual patterns, table for exact numbers.
- Use QueryAzure for ARM APIs, QueryGraph for Microsoft Graph, QueryLogAnalytics for KQL — these use the user's delegated tokens.
- For retail pricing, use the built-in fetch tool with https://prices.azure.com (public, no auth). Always filter by armRegionName + serviceName + armSkuName and use $top=20.
- For Azure AI Foundry / Azure OpenAI questions (model deployments, quota usage, available models, capacity), use QueryAzure with the Microsoft.CognitiveServices APIs — see the QueryAzure tool description for the exact paths (accounts, deployments, models, locations/{region}/usages). For quota questions per region the canonical endpoint is GET /subscriptions/{id}/providers/Microsoft.CognitiveServices/locations/{region}/usages?api-version=2026-03-01 (NOTE: when bumping this api-version, also update the matching entry in AzureQueryTools.cs and the API-versions summary line in .github/copilot-instructions.md). For per-token retail pricing, prefer prices.azure.com with serviceName eq 'Foundry Models'; if a very new model (e.g. a just-released gpt-X.Y) returns no meters, tell the user it is not yet published in the public Retail Prices API and link them to https://azure.microsoft.com/pricing/details/cognitive-services/openai-service/.
- When the user asks for a repeatable check (""give me a script for this"", ""how do I run this myself""), call GenerateScript to produce a downloadable az CLI / PowerShell script wrapping the same QueryAzure calls.
- Wait for tool results before rendering charts — never render with empty data.
- Call independent tools in parallel (e.g. QueryAzure + QueryGraph simultaneously).
- After answering a public FinOps question, call PublishFAQ to save it as an SEO page. Never publish tenant-specific data.

## Large Data Strategy
APIs can return massive payloads. Follow this hierarchy:
1. **Scope at the source** — each tool description tells you how to filter, group, and limit. ALWAYS aggregate in the query itself (grouping, summarize, $top, $select). Never request raw ungrouped data.
2. **Python post-processing** — when a response is still large or needs transformation (pivoting, derived metrics, multi-source joins), save the JSON to a file and run a Python script with pandas/numpy to process it. Don't try to reason over 100KB+ of raw JSON.
3. **Drill-down pattern** — start with a high-level aggregated query to understand the shape, then drill into the top items with targeted queries.
";

// ── Shared (stateless) AI tools — safe to share across all users ──
var sharedTools = new List<AIFunction>();
var chartLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AzureFinOps.AI.Charts");
sharedTools.AddRange(ChartTools.Create(chartLogger));
sharedTools.AddRange(HealthTools.Create());
sharedTools.AddRange(PresentationTools.Create());
sharedTools.AddRange(FollowUpTools.Create());
sharedTools.AddRange(FaqTools.Create());
sharedTools.AddRange(ScoreTools.Create());
sharedTools.AddRange(ScriptTools.Create());
sharedTools.AddRange(ScheduleTools.Create());
sharedTools.AddRange(RetailPricingTools.Create()); // public Azure Retail Prices API — no auth

// Per-user token holders and tool lists — tools capture the UserTokens instance
// via closure, so they always read the latest tokens regardless of thread.

List<AIFunction> GetOrCreateUserTools(long userId)
{
    return userTools.GetOrAdd(userId, uid =>
    {
        var tokens = userTokens.GetOrAdd(uid, _ => new UserTokens());
        var tools = new List<AIFunction>(sharedTools);
        tools.AddRange(new AzureQueryTools(tokens).Create());
        tools.AddRange(new GraphQueryTools(tokens).Create());
        tools.AddRange(new LogAnalyticsQueryTools(tokens).Create());
        tools.AddRange(new StorageQueryTools(tokens).Create());
        tools.AddRange(new AnomalyTools(tokens).Create());
        tools.AddRange(new IdleResourceTools(tokens).Create());
        return tools;
    });
}

// Create a Copilot SessionConfig with BYOK provider using a fresh Entra ID bearer token
async Task<SessionConfig> CreateSessionConfigAsync(long userId)
{
    var bearerToken = await GetAzureOpenAIBearerTokenAsync();
    return new SessionConfig
    {
        Model = azureOpenAIDeployment,
        ReasoningEffort = "xhigh",
        Streaming = true,
        Tools = GetOrCreateUserTools(userId),
        OnPermissionRequest = (_, _) => Task.FromResult(new PermissionRequestResult { Kind = PermissionRequestResultKind.Approved }),
        Provider = new ProviderConfig
        {
            Type = "azure",
            BaseUrl = azureOpenAIEndpoint.TrimEnd('/'),
            BearerToken = bearerToken,
        },
        SystemMessage = new SystemMessageConfig
        {
            Mode = SystemMessageMode.Append,
            Content = SystemPrompt,
        },
    };
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
        tokens.StorageToken = await GetStorageTokenAsync(ctx, httpFactory);
    }
    finally
    {
        tokens.RefreshLock.Release();
    }

    logger.LogInformation("Chat tokens: azure={HasAzure} graph={HasGraph} la={HasLA} storage={HasStorage}",
        tokens.AzureToken is not null, tokens.GraphToken is not null, tokens.LogAnalyticsToken is not null, tokens.StorageToken is not null);

    // Inject connection context so the LLM knows the user's actual state
    var connectedApis = new List<string>();
    if (tokens.AzureToken is not null) connectedApis.Add("Azure ARM (QueryAzure)");
    if (tokens.GraphToken is not null) connectedApis.Add("Microsoft Graph (QueryGraph)");
    if (tokens.LogAnalyticsToken is not null) connectedApis.Add("Log Analytics (QueryLogAnalytics)");
    if (tokens.StorageToken is not null) connectedApis.Add("Azure Storage (ListCostExportBlobs, ReadCostExportBlob)");
    var connectionContext = connectedApis.Count > 0
        ? $"[CONTEXT: User IS connected to Azure. Available APIs: {string.Join(", ", connectedApis)}. Proceed with tool calls directly.]"
        : "[CONTEXT: User is NOT connected to Azure. You can still answer any question that does NOT require their tenant-specific data — including public Azure information (regions, datacenters, services, pricing via RetailPrices, service health, general FinOps guidance), rendering charts/maps with public data, and explaining concepts. Use your built-in knowledge and public tools (RenderChart, RenderAdvancedChart, RetailPricing, GetAzureServiceHealth, web fetch) freely. Only ask the user to click 'Connect Azure' when the question genuinely requires their subscription/tenant data (their costs, their resources, their usage). Do NOT refuse public questions.]";
    prompt = $"{connectionContext}\n{prompt}";

    // SSE headers
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";

    try
    {
        // Get or create per-user CopilotSession
        var sessionConfig = await CreateSessionConfigAsync(userId);

        if (!userSessions.TryGetValue(userId, out var session))
        {
            session = await copilotClient.CreateSessionAsync(sessionConfig);
            userSessions[userId] = session;
            activeSessionsGauge.Add(1);
            logger.LogInformation("Created new Copilot session for {User} sessionId={SessionId}", userLogin, session.SessionId);
        }

        var done = new TaskCompletionSource();
        var cancelled = false;
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

        // Event handler for SSE streaming
        using var subscription = session.On(async (SessionEvent evt) =>
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

                    // Chart detection (RenderChart / RenderAdvancedChart)
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
                            foreach (var line in resultText.Split('\n'))
                            {
                                var trimmed = line.Trim();
                                if (trimmed.StartsWith("__CHART__:"))
                                {
                                    var chartJson = trimmed["__CHART__:".Length..].Trim();
                                    await ctx.Response.WriteAsync($"data: {sseData}\n\n");
                                    await ctx.Response.Body.FlushAsync();
                                    var chartPayload = JsonSerializer.Serialize(new { type = "chart", options = chartJson });
                                    await ctx.Response.WriteAsync($"data: {chartPayload}\n\n");
                                    await ctx.Response.Body.FlushAsync();
                                    sseData = null;
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
                    // PPTX detection
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
                    // Script detection
                    if (toolDone.Data.Success && resultText is not null && resultText.Contains("__SCRIPT_READY__:"))
                    {
                        try
                        {
                            foreach (var line in resultText.Split('\n'))
                            {
                                var trimmed = line.Trim();
                                if (trimmed.StartsWith("__SCRIPT_READY__:"))
                                {
                                    var parts = trimmed["__SCRIPT_READY__:".Length..].Split(':', 5);
                                    if (parts.Length >= 4)
                                    {
                                        // Retrieve script content for inline preview
                                        var scriptFileId = parts[0];
                                        var scriptContent = "";
                                        if (ScriptTools.GeneratedFiles.TryGetValue(scriptFileId, out var scriptEntry))
                                            scriptContent = scriptEntry.Content ?? "";
                                        var scriptPayload = JsonSerializer.Serialize(new { type = "script_ready", fileId = parts[0], fileName = parts[1], lineCount = parts[2], language = parts[3], description = parts.Length > 4 ? parts[4] : "", content = scriptContent });
                                        if (sseData is not null)
                                        {
                                            await ctx.Response.WriteAsync($"data: {sseData}\n\n");
                                            await ctx.Response.Body.FlushAsync();
                                        }
                                        await ctx.Response.WriteAsync($"data: {scriptPayload}\n\n");
                                        await ctx.Response.Body.FlushAsync();
                                        sseData = null;
                                    }
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
                    // Maturity score detection
                    if (toolDone.Data.Success && resultText is not null && resultText.Contains("__MATURITY_SCORE__:"))
                    {
                        try
                        {
                            var trimmed = resultText.Trim();
                            if (trimmed.StartsWith("__MATURITY_SCORE__:"))
                            {
                                var rest = trimmed["__MATURITY_SCORE__:".Length..];
                                var colonIdx = rest.IndexOf(':');
                                if (colonIdx > 0)
                                {
                                    var level = rest[..colonIdx];
                                    var scoresJson = rest[(colonIdx + 1)..];
                                    var scorePayload = JsonSerializer.Serialize(new { type = "maturity_score", level, scores = scoresJson });
                                    if (sseData is not null)
                                    {
                                        await ctx.Response.WriteAsync($"data: {sseData}\n\n");
                                        await ctx.Response.Body.FlushAsync();
                                    }
                                    await ctx.Response.WriteAsync($"data: {scorePayload}\n\n");
                                    await ctx.Response.Body.FlushAsync();
                                    sseData = null;
                                }
                            }
                        }
                        catch { }
                    }
                }
                else if (evt is SessionErrorEvent error)
                {
                    sseData = JsonSerializer.Serialize(new { type = "error", message = error.Data.Message });
                    logger.LogError("Session error for {User}: {Error}", userLogin, error.Data.Message);
                    chatActivity?.SetTag("ai.error", error.Data.Message);
                    // Remove stale session on error
                    userSessions.TryRemove(userId, out _);
                    activeSessionsGauge.Add(-1);
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
        });

        try
        {
            await session.SendAsync(new MessageOptions { Prompt = prompt });
        }
        catch (Exception sendEx) when (sendEx.Message.Contains("Session not found", StringComparison.OrdinalIgnoreCase))
        {
            // Session expired on the Copilot CLI side — recreate and retry
            logger.LogWarning("Copilot session expired for user {User}, recreating. Error: {Error}", userLogin, sendEx.Message);
            chatActivity?.SetTag("ai.session_expired", true);
            if (userSessions.TryRemove(userId, out _)) activeSessionsGauge.Add(-1);

            var freshConfig = await CreateSessionConfigAsync(userId);
            session = await copilotClient.CreateSessionAsync(freshConfig);
            userSessions[userId] = session;
            activeSessionsGauge.Add(1);
            logger.LogInformation("Recreated Copilot session for {User} sessionId={SessionId}", userLogin, session.SessionId);

            await session.SendAsync(new MessageOptions { Prompt = prompt });
        }

        try { await done.Task; }
        finally { /* subscription disposed by using */ }

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

    // Dispose and remove the CopilotSession — next chat creates a fresh one
    if (userSessions.TryRemove(userId, out var oldSession))
    {
        activeSessionsGauge.Add(-1);
        try { await oldSession.DisposeAsync(); } catch { }
    }

    logger.LogInformation("Copilot session cleared for user {UserId}", userId);

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
// SCRIPT DOWNLOAD
// ──────────────────────────────────────────────
app.MapGet("/api/download/script/{fileId}", (string fileId) =>
{
    if (!ScriptTools.GeneratedFiles.TryGetValue(fileId, out var entry))
        return Results.NotFound(new { error = "File not found or expired" });

    if (!File.Exists(entry.Path))
    {
        ScriptTools.GeneratedFiles.TryRemove(fileId, out _);
        return Results.NotFound(new { error = "File no longer available" });
    }

    var fileName = Path.GetFileName(entry.Path);
    var downloadName = fileName.Contains('_') ? fileName[(fileName.IndexOf('_') + 1)..] : fileName;
    var bytes = File.ReadAllBytes(entry.Path);

    var contentType = downloadName.EndsWith(".ps1") ? "application/x-powershell" : "application/x-shellscript";

    return Results.File(bytes, contentType, downloadName);
});

// ──────────────────────────────────────────────
// SEO FAQ PAGES (server-rendered HTML for search engines)
// ──────────────────────────────────────────────
var faqPages = new Dictionary<string, (string Title, string Question, string Answer, string Prompt)>(StringComparer.OrdinalIgnoreCase)
{
    ["azure-vm-pricing"] = (
        "Azure VM Pricing by Region — D4s_v5 Monthly Cost Comparison",
        "How much does a D4s_v5 VM cost per month on Azure?",
        "A Standard_D4s_v5 VM (4 vCPUs, 16 GB RAM) on Azure costs approximately $98–$168/month depending on the region. The cheapest regions are typically US Central, Central India, and US West. Pricing is calculated at the hourly pay-as-you-go rate × 730 hours/month. Use Azure Reserved Instances for 30–65% savings on steady-state workloads.",
        "Compare the monthly cost of a D4s_v5 VM across the 10 cheapest Azure regions. Show a bar chart."),
    ["spot-vs-on-demand"] = (
        "Azure Spot vs On-Demand VM Pricing — Savings Comparison",
        "What is the difference between Azure spot and on-demand VM pricing?",
        "Azure spot VMs offer up to 80% discount compared to on-demand pricing, but can be evicted when Azure needs capacity. For example, a D4s_v5 costs ~$0.192/hr on-demand vs ~$0.038/hr spot (80% savings). Spot is ideal for fault-tolerant workloads like batch processing, CI/CD, and dev/test environments.",
        "Compare spot vs on-demand pricing for D4s_v5, D8s_v5, and NC24ads_A100_v4 in East US."),
    ["reserved-instances"] = (
        "Azure Reserved Instances vs Pay-As-You-Go — Pricing Comparison",
        "How do Azure Reserved Instances compare to pay-as-you-go?",
        "Azure Reserved Instances offer 30–40% savings for 1-year commitments and 55–65% savings for 3-year commitments compared to pay-as-you-go pricing. They're best for predictable, steady-state workloads running 24/7. Savings Plans offer similar discounts with more flexibility across VM families and regions.",
        "Compare pay-as-you-go vs 1-year vs 3-year reserved pricing for a D4s_v5 VM in East US."),
    ["finops-azure"] = (
        "What is FinOps? — Azure Cloud Financial Management Guide",
        "What is FinOps and how does it apply to Azure?",
        "FinOps is a cloud financial management discipline combining finance, technology, and business to optimize cloud spending. On Azure, key FinOps practices include: right-sizing VMs based on Advisor recommendations, using reservations and savings plans for committed workloads, cleaning up idle resources (unattached disks, unused IPs), implementing cost allocation tags, setting budgets with alerts, and running regular cost reviews.",
        "Conduct a FinOps maturity assessment of my Azure environment."),
    ["idle-resources"] = (
        "Find Idle Azure Resources — Cost Optimization Guide",
        "How can I find idle or unused Azure resources to reduce costs?",
        "Common idle resources include: unattached managed disks, unused public IPs, empty resource groups, VMs with consistently low CPU (<5%), App Service plans with no apps, orphaned NICs, and NSGs not attached to subnets. Azure Advisor provides cost recommendations, and Azure FinOps Agent can scan all subscriptions simultaneously to quantify the total waste.",
        "Find all idle or underutilized VMs, disks, public IPs, and App Service plans across my subscriptions."),
    ["storage-tier-pricing"] = (
        "Azure Blob Storage Tier Pricing — Hot, Cool, Cold, Archive Comparison",
        "How do Azure Blob Storage tiers compare in pricing?",
        "Azure Blob Storage tiers from most to least expensive for storage: Hot (~$0.018/GB), Cool (~$0.01/GB, 30-day minimum), Cold (~$0.0036/GB, 90-day minimum), Archive (~$0.00099/GB, 180-day minimum). Access costs are inverse — Archive has the highest retrieval fees and hours-long rehydration. Use lifecycle management policies to automatically tier data based on access patterns.",
        "Compare Azure Blob Storage costs for 10 TB across Hot, Cool, Cold, and Archive tiers in East US."),
    ["aks-vs-container-apps"] = (
        "AKS vs Container Apps vs Azure Functions — Cost Comparison",
        "What is the cost of running microservices on AKS vs Container Apps vs Functions?",
        "AKS charges only for underlying VMs (control plane is free). Container Apps has consumption billing per vCPU-second and GB-second. Functions consumption plan charges per execution and GB-second. For 20 microservices: AKS is cheapest at scale (~$300–600/mo), Container Apps is simplest for moderate traffic (~$200–800/mo), Functions is cheapest for bursty/infrequent workloads (~$50–300/mo).",
        "Compare cost of running 20 microservices on AKS vs Azure Container Apps vs Azure Functions."),
    ["gpu-training-cost"] = (
        "Azure GPU Training Cost — A100 vs H100 Pricing Comparison",
        "How much does GPU training cost on Azure (A100 vs H100)?",
        "The ND96asr_v4 (8× A100 80GB) costs ~$27–32/hr on-demand. The NC80adis_H100_v5 (8× H100) costs ~$32–38/hr. Spot pricing reduces costs 60–80% when available. A 4-node training cluster costs $80K–110K/month on-demand, or $16K–30K with spot instances. H100 offers ~2× training throughput vs A100, making cost-per-training-step often comparable.",
        "Compare monthly cost of 4x A100 (ND96asr_v4) vs 4x H100 (NC80adis_H100_v5) on-demand."),
};

app.MapGet("/faq/{slug}", (string slug) =>
{
    // Try static FAQ first, then dynamic
    string title, question, answer, prompt, date;
    if (faqPages.TryGetValue(slug, out var page))
    {
        title = page.Title; question = page.Question; answer = page.Answer; prompt = page.Prompt; date = "2026-03-29";
    }
    else if (FaqTools.TryGet(slug, out var dynamic))
    {
        title = dynamic.Title; question = dynamic.Question; answer = dynamic.Answer; prompt = question; date = dynamic.CreatedUtc;
    }
    else
    {
        return Results.NotFound("FAQ page not found") as IResult;
    }

    Func<string?, string> e = System.Net.WebUtility.HtmlEncode!;
    var desc = answer.Length > 155 ? answer[..155] + "..." : answer;
    var faqUrl = $"https://azure-finops-agent.com/faq/{slug}";
    var isoDate = date + "T00:00:00+00:00";
    var jsonLd = JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "QAPage",
        ["mainEntity"] = new Dictionary<string, object>
        {
            ["@type"] = "Question",
            ["name"] = question,
            ["text"] = question,
            ["answerCount"] = 1,
            ["dateCreated"] = isoDate,
            ["datePublished"] = isoDate,
            ["author"] = new Dictionary<string, object>
            {
                ["@type"] = "Organization",
                ["name"] = "Azure FinOps Agent",
                ["url"] = "https://azure-finops-agent.com"
            },
            ["acceptedAnswer"] = new Dictionary<string, object>
            {
                ["@type"] = "Answer",
                ["text"] = answer,
                ["dateCreated"] = isoDate,
                ["datePublished"] = isoDate,
                ["upvoteCount"] = 1,
                ["url"] = faqUrl,
                ["author"] = new Dictionary<string, object>
                {
                    ["@type"] = "Organization",
                    ["name"] = "Azure FinOps Agent",
                    ["url"] = "https://azure-finops-agent.com"
                }
            }
        }
    });
    var html = "<!DOCTYPE html><html lang=\"en\"><head>"
        + "<meta charset=\"UTF-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1.0\">"
        + "<title>" + e(title) + "</title>"
        + "<meta name=\"description\" content=\"" + e(desc) + "\">"
        + "<meta name=\"robots\" content=\"index, follow\">"
        + "<link rel=\"canonical\" href=\"" + faqUrl + "\">"
        + "<meta property=\"og:type\" content=\"article\">"
        + "<meta property=\"og:title\" content=\"" + e(title) + "\">"
        + "<meta property=\"og:url\" content=\"" + faqUrl + "\">"
        + "<script type=\"application/ld+json\">" + jsonLd + "</script>"
        + "<style>body{font-family:Segoe UI,system-ui,sans-serif;max-width:800px;margin:0 auto;padding:2rem 1rem;color:#1a1a2e;line-height:1.7}h1{font-size:1.6rem;color:#0078d4}h2{font-size:1.2rem;margin-top:2rem}.answer{background:#f0f6ff;border-left:4px solid #0078d4;padding:1rem 1.5rem;border-radius:0 8px 8px 0;margin:1.5rem 0}.cta{display:inline-block;background:#0078d4;color:#fff;padding:0.75rem 1.5rem;border-radius:8px;text-decoration:none;margin-top:1.5rem;font-weight:600}.cta:hover{background:#106ebe}footer{margin-top:3rem;font-size:0.85rem;color:#888}</style>"
        + "</head><body>"
        + "<h1>" + e(title) + "</h1>"
        + "<h2>" + e(question) + "</h2>"
        + "<div class=\"answer\"><p>" + e(answer) + "</p></div>"
        + "<p>Want a live, interactive answer with real-time Azure pricing data and charts?</p>"
        + "<a class=\"cta\" href=\"/?q=" + Uri.EscapeDataString(prompt) + "\">Ask the FinOps Agent</a>"
        + "<footer><p>Azure FinOps Agent &mdash; AI-powered cloud cost optimization. <a href=\"/\">Back to home</a></p>"
        + "<p>Pricing data from <a href=\"https://prices.azure.com\">Azure Retail Prices API</a>. Prices may vary.</p></footer>"
        + "</body></html>";

    return Results.Content(html, "text/html; charset=utf-8");
});

// FAQ index page
app.MapGet("/faq", () =>
{
    Func<string?, string> e = System.Net.WebUtility.HtmlEncode!;
    var listItems = string.Join("", faqPages.Select(kv =>
        "<li><a href=\"/faq/" + kv.Key + "\">" + e(kv.Value.Question) + "</a></li>"));
    // Append dynamic FAQ entries
    var dynamicItems = string.Join("", FaqTools.GetAll().Select(kv =>
        "<li><a href=\"/faq/" + kv.Key + "\">" + e(kv.Value.Question) + "</a> <small style=\"color:#888\">(community)</small></li>"));
    listItems += dynamicItems;

    var html = "<!DOCTYPE html><html lang=\"en\"><head>"
        + "<meta charset=\"UTF-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1.0\">"
        + "<title>Azure FinOps FAQ &mdash; Cloud Cost Optimization Questions &amp; Answers</title>"
        + "<meta name=\"description\" content=\"Frequently asked questions about Azure cloud cost optimization, VM pricing, reserved instances, FinOps best practices, and cost management.\">"
        + "<meta name=\"robots\" content=\"index, follow\">"
        + "<link rel=\"canonical\" href=\"https://azure-finops-agent.com/faq\">"
        + "<style>body{font-family:Segoe UI,system-ui,sans-serif;max-width:800px;margin:0 auto;padding:2rem 1rem;color:#1a1a2e;line-height:1.7}h1{color:#0078d4}ul{padding-left:1.2rem}li{margin:0.75rem 0}a{color:#0078d4}.cta{display:inline-block;background:#0078d4;color:#fff;padding:0.75rem 1.5rem;border-radius:8px;text-decoration:none;margin-top:1.5rem;font-weight:600}</style>"
        + "</head><body>"
        + "<h1>Azure FinOps FAQ</h1>"
        + "<p>Common questions about Azure cloud cost optimization, answered with real pricing data.</p>"
        + "<ul>" + listItems + "</ul>"
        + "<a class=\"cta\" href=\"/\">Try the FinOps Agent</a>"
        + "<footer style=\"margin-top:3rem;font-size:0.85rem;color:#888\"><p>Azure FinOps Agent &mdash; AI-powered cloud cost optimization. <a href=\"/\">Home</a></p></footer>"
        + "</body></html>";

    return Results.Content(html, "text/html");
});

// Dynamic sitemap that includes static + community FAQs
app.MapGet("/sitemap.xml", () =>
{
    var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
    var urls = "<url><loc>https://azure-finops-agent.com/</loc><lastmod>" + today + "</lastmod><changefreq>weekly</changefreq><priority>1.0</priority></url>"
        + "<url><loc>https://azure-finops-agent.com/faq</loc><lastmod>" + today + "</lastmod><changefreq>weekly</changefreq><priority>0.9</priority></url>";

    foreach (var kv in faqPages)
        urls += "<url><loc>https://azure-finops-agent.com/faq/" + kv.Key + "</loc><lastmod>" + today + "</lastmod><changefreq>monthly</changefreq><priority>0.8</priority></url>";

    foreach (var kv in FaqTools.GetAll())
        urls += "<url><loc>https://azure-finops-agent.com/faq/" + kv.Key + "</loc><lastmod>" + kv.Value.CreatedUtc + "</lastmod><changefreq>monthly</changefreq><priority>0.7</priority></url>";

    var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">" + urls + "</urlset>";
    return Results.Content(xml, "application/xml");
});

// ──────────────────────────────────────────────
// SPA FALLBACK
// ──────────────────────────────────────────────
app.MapFallbackToFile("index.html");

app.Run();

