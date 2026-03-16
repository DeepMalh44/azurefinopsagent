using System.Collections.Concurrent;
using System.Diagnostics;
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

// OpenTelemetry + Application Insights
var appInsightsCs = builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrEmpty(appInsightsCs))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(o => o.ConnectionString = appInsightsCs);
}

// ActivitySource for custom AI telemetry spans
var aiActivitySource = new ActivitySource("AzureFinOps.AI");

var app = builder.Build();
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AzureFinOps.AI");

// Trust forwarded headers from Azure App Service reverse proxy (X-Forwarded-Proto, X-Forwarded-For)
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseSession();

// Serve Vue SPA from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

var gitHubClientId = app.Configuration["GitHub:ClientId"]!;
var gitHubClientSecret = app.Configuration["GitHub:ClientSecret"]!;

// ──────────────────────────────────────────────
// AUTH ENDPOINTS
// ──────────────────────────────────────────────

// Redirect to GitHub OAuth
app.MapGet("/auth/github", (HttpContext ctx) =>
{
    var state = Guid.NewGuid().ToString("N");
    ctx.Session.SetString("oauth_state", state);

    var callbackUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}/auth/github/callback";
    // GitHub App OAuth — permissions are configured on the app itself, not via scopes.
    // This avoids the classic OAuth App consent screen that shows "access all repos".
    var url = $"https://github.com/login/oauth/authorize?client_id={gitHubClientId}&redirect_uri={Uri.EscapeDataString(callbackUrl)}&state={state}";
    return Results.Redirect(url);
});

// GitHub OAuth callback
app.MapGet("/auth/github/callback", async (HttpContext ctx, IHttpClientFactory httpFactory) =>
{
    try
    {
        var code = ctx.Request.Query["code"].ToString();
        var state = ctx.Request.Query["state"].ToString();

        if (state != ctx.Session.GetString("oauth_state"))
            return Results.StatusCode(403);

        ctx.Session.Remove("oauth_state");

        var http = httpFactory.CreateClient();

        // Exchange code for token
        var tokenReq = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token");
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
            return Results.Problem($"GitHub token exchange returned {(int)tokenRes.StatusCode}: {tokenBody}");

        var tokenJson = JsonSerializer.Deserialize<JsonElement>(tokenBody);

        if (tokenJson.TryGetProperty("error", out var err))
            return Results.Problem($"OAuth token exchange failed: {err}");

        if (!tokenJson.TryGetProperty("access_token", out var tokenProp))
            return Results.Problem($"No access_token in GitHub response: {tokenBody}");

        var accessToken = tokenProp.GetString()!;

        // Store refresh token if provided (GitHub App tokens expire)
        var refreshToken = tokenJson.TryGetProperty("refresh_token", out var rtProp) ? rtProp.GetString() : null;

        // Validate token scopes
        var scopeReq = new HttpRequestMessage(HttpMethod.Head, "https://api.github.com/user");
        scopeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        scopeReq.Headers.UserAgent.ParseAdd("FinOps-Dashboard/1.0");
        var scopeRes = await http.SendAsync(scopeReq);
        var grantedScopes = scopeRes.Headers.Contains("X-OAuth-Scopes")
            ? scopeRes.Headers.GetValues("X-OAuth-Scopes").FirstOrDefault() ?? ""
            : "";
        Console.WriteLine($"[Auth] Token scopes: {grantedScopes}");
        ctx.Session.SetString("token_scopes", grantedScopes);

        // Fetch user profile
        var userReq = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
        userReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        userReq.Headers.UserAgent.ParseAdd("FinOps-Dashboard/1.0");

        var userRes = await http.SendAsync(userReq);
        var userBody = await userRes.Content.ReadAsStringAsync();

        if (!userRes.IsSuccessStatusCode)
            return Results.Problem($"GitHub user API returned {(int)userRes.StatusCode}: {userBody}");

        var userJson = JsonSerializer.Deserialize<JsonElement>(userBody);
        var login = userJson.GetProperty("login").GetString()!;

        // Store in session
        ctx.Session.SetString("github_token", accessToken);
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
        Console.Error.WriteLine($"OAuth callback error: {ex}");
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
app.MapPost("/auth/logout", (HttpContext ctx) =>
{
    ctx.Session.Clear();
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
    var req = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token");
    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    req.Content = JsonContent.Create(new
    {
        client_id = gitHubClientId,
        client_secret = gitHubClientSecret,
        grant_type = "refresh_token",
        refresh_token = refreshToken
    });

    var res = await http.SendAsync(req);
    if (!res.IsSuccessStatusCode) return null;

    var body = await res.Content.ReadAsStringAsync();
    var json = JsonSerializer.Deserialize<JsonElement>(body);
    if (json.TryGetProperty("error", out _)) return null;
    if (!json.TryGetProperty("access_token", out var newToken)) return null;

    var newAccessToken = newToken.GetString()!;
    ctx.Session.SetString("github_token", newAccessToken);
    if (json.TryGetProperty("refresh_token", out var newRt))
        ctx.Session.SetString("github_refresh_token", newRt.GetString()!);

    Console.WriteLine("[Auth] Token refreshed successfully");
    return newAccessToken;
}

// Per-user CopilotClient + CopilotSession cache
var clients = new ConcurrentDictionary<long, CopilotClient>();
var sessions = new ConcurrentDictionary<long, CopilotSession>();
// Track which token each client was created with (so we can detect changes and recreate)
var clientTokens = new ConcurrentDictionary<long, string>();
// Track which model each session was created with
var sessionModels = new ConcurrentDictionary<long, string>();

// ── Register all AI tools ──
var agentTools = new List<AIFunction>();
agentTools.AddRange(ChartTools.Create());
agentTools.AddRange(PricingTools.Create());
agentTools.AddRange(HealthTools.Create());
agentTools.AddRange(CodeExecutionTools.Create());

app.MapPost("/api/chat", (Delegate)(async (HttpContext ctx, IHttpClientFactory httpFactory) =>
{
    var githubToken = ctx.Session.GetString("github_token");
    var userJson = ctx.Session.GetString("user");
    if (githubToken is null || userJson is null)
    {
        // Try refreshing the token before returning 401
        var refreshed = await RefreshGitHubTokenAsync(ctx, httpFactory);
        if (refreshed is not null)
        {
            githubToken = refreshed;
        }
        else if (githubToken is null || userJson is null)
        {
            ctx.Response.StatusCode = 401;
            return;
        }
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

    using var chatActivity = aiActivitySource.StartActivity("ChatRequest");
    chatActivity?.SetTag("ai.user", userLogin);
    chatActivity?.SetTag("ai.model", model);
    chatActivity?.SetTag("ai.prompt", prompt!.Length > 500 ? prompt[..500] + "..." : prompt);
    logger.LogInformation("Chat request from {User} model={Model} prompt={Prompt}", userLogin, model, prompt.Length > 200 ? prompt[..200] + "..." : prompt);

    // Set GH_TOKEN BEFORE creating/starting the CopilotClient so the CLI child process
    // inherits it at spawn time.
    Environment.SetEnvironmentVariable("GH_TOKEN", githubToken);

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
            Console.WriteLine($"[Chat] Token changed for user {userId}, recreating CopilotClient");
            if (clients.TryRemove(userId, out var old)) try { old.Dispose(); } catch { }
            sessions.TryRemove(userId, out _);
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

        if (!sessions.TryGetValue(userId, out var session))
        {
            session = await client.CreateSessionAsync(new SessionConfig
            {
                Model = model,
                Streaming = true,
                Tools = agentTools,
                OnPermissionRequest = (_, _) => Task.FromResult(new PermissionRequestResult { Kind = PermissionRequestResultKind.Approved }),
                SystemMessage = new SystemMessageConfig
                {
                    Mode = SystemMessageMode.Append,
                    Content = @"
You are the Azure FinOps Agent — a concise, data-driven AI assistant for Azure cost optimization.

## Tools
- **FetchUrl** — HTTP GET any allowed Azure API URL. Returns raw JSON.
- **GetAzureServiceHealth** — Current Azure service health status and incidents. No params.
- **RenderChart** — Render one interactive chart (bar, line, pie, scatter, funnel) per response.
- **RunScript** — Execute Python 3, bash, or SQLite scripts on the server. Use for data processing, filtering, aggregation, or any computation. The server has python3 with pandas, numpy, and sqlite3 installed.

## Data Sources (no auth required)
- Azure Retail Prices API: `https://prices.azure.com/api/retail/prices` — supports OData `$filter` and `$top`.
- Azure Service Health RSS feed.

## How to Work
1. Fetch data with FetchUrl or GetAzureServiceHealth.
2. Use RunScript to process, filter, aggregate, or transform data (python3, bash, sqlite3 are available).
3. Present results concisely. Use tables over prose. Visualize with RenderChart when helpful.
4. Max ONE chart per response. Offer to show more.
5. For RunScript, prefer Python with pandas for data analysis. Write complete scripts that print results to stdout.
"
                },
            });
            sessions[userId] = session;
            sessionModels[userId] = model!;
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

        // Subscribe per-request event handler (IDisposable for cleanup after response)
        using var subscription = session.On(async evt =>
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
                    sseData = JsonSerializer.Serialize(new { type = "tool_done", tool = toolName, id = toolId, success = toolDone.Data.Success, durationMs, result = resultText, error = errorText });
                    logger.LogInformation("Tool done: {Tool} id={ToolId} success={Success} durationMs={Duration} resultLen={ResultLen}",
                        toolName, toolId, toolDone.Data.Success, durationMs, resultText?.Length ?? 0);

                    // If this is a RenderChart tool completion, also emit a chart event
                    if (toolName == "RenderChart" && toolDone.Data.Success && resultText is not null)
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
                }
                else if (evt is SessionErrorEvent error)
                {
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
        });

        await session.SendAsync(new MessageOptions { Prompt = prompt });
        await done.Task;
    }
    catch (Exception ex)
    {
        sessions.TryRemove(userId, out _);
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

    if (sessions.TryRemove(userId, out var oldSession))
    {
        try { await oldSession.DisposeAsync(); } catch { }
    }
    if (clients.TryRemove(userId, out var old))
    {
        try { await old.DisposeAsync(); } catch { }
    }

    ctx.Response.StatusCode = 204;
});

// ──────────────────────────────────────────────
// MODELS ENDPOINT
// ──────────────────────────────────────────────

app.MapGet("/api/models", async (HttpContext ctx, IHttpClientFactory httpFactory) =>
{
    var githubToken = ctx.Session.GetString("github_token");
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
    var user = JsonSerializer.Deserialize<JsonElement>(userJson!);
    var userId = user.GetProperty("id").GetInt64();

    try
    {
        Environment.SetEnvironmentVariable("GH_TOKEN", githubToken);

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
// SPA FALLBACK
// ──────────────────────────────────────────────
app.MapFallbackToFile("index.html");

app.Run();
