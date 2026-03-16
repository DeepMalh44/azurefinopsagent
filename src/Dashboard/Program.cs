using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
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

var app = builder.Build();

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
    var url = $"https://github.com/login/oauth/authorize?client_id={gitHubClientId}&redirect_uri={Uri.EscapeDataString(callbackUrl)}&scope=read:user%20user:email%20copilot&state={state}";
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

// Per-user CopilotClient + CopilotSession cache
var clients = new ConcurrentDictionary<long, CopilotClient>();
var sessions = new ConcurrentDictionary<long, CopilotSession>();
// Track which token each client was created with (so we can detect changes and recreate)
var clientTokens = new ConcurrentDictionary<long, string>();

// ── Register all AI tools ──
var agentTools = new List<AIFunction>();
agentTools.AddRange(ChartTools.Create());
agentTools.AddRange(WeatherTools.Create());

app.MapPost("/api/chat", (Delegate)(async (HttpContext ctx) =>
{
    var githubToken = ctx.Session.GetString("github_token");
    var userJson = ctx.Session.GetString("user");
    if (githubToken is null || userJson is null)
    {
        ctx.Response.StatusCode = 401;
        return;
    }

    using var bodyDoc = await JsonDocument.ParseAsync(ctx.Request.Body);
    var prompt = bodyDoc.RootElement.GetProperty("prompt").GetString();
    var model = bodyDoc.RootElement.TryGetProperty("model", out var m) ? m.GetString() : "claude-sonnet-4.6";

    if (string.IsNullOrWhiteSpace(prompt))
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { error = "prompt is required" });
        return;
    }

    var user = JsonSerializer.Deserialize<JsonElement>(userJson);
    var userId = user.GetProperty("id").GetInt64();

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
You are the Azure FinOps Agent AI assistant. Be concise — short paragraphs, bullet points, data-first.

## Environment
You run as a server-side agent inside a .NET backend. Your sandbox provides:
- **sql** — In-memory SQLite database for analytical queries.
- **shell (bash/powershell)** — Full Linux sandbox with rich tooling.
- **task** — Sub-agent for delegating complex multi-step work.
- **Custom tools** — Weather API (Open-Meteo) and chart rendering (ECharts).

## Available Custom Tools
- **GetCurrentWeather** — Fetches current weather conditions for a given location (latitude/longitude). Returns temperature, wind speed, humidity, and weather condition.
- **GetWeatherForecast** — Fetches a 7-day weather forecast for a given location. Returns daily min/max temperatures, precipitation, and weather codes.
- **RenderChart** — Renders interactive ECharts charts in the dashboard UI. Supports: bar, line, pie, scatter, funnel.

## Weather Tool Usage
- Use Open-Meteo tools to answer weather questions. The API is free, no key required.
- For city names, convert to latitude/longitude first using your knowledge (e.g., New York = 40.71, -74.01).
- Always include the location name in your response so the user knows which city the data is for.
- When showing forecasts, consider using RenderChart to visualize temperature trends.

## Response Rules
- **Max ONE RenderChart per response.** Pick the most impactful chart. Offer to show more if needed.
- Chart types: bar=comparisons, line=trends, pie=proportions, scatter=correlations.
- Data format: JSON array string, e.g. [[""Label1"", 100], [""Label2"", 200]].
- Keep text brief. Tables over prose. Only elaborate when asked.

## Large Data Workflow (SQL)
When a request involves many records:
1. **Fetch** the data using the appropriate tool.
2. **Insert** the fetched data into the in-memory SQL database using the `sql` tool.
3. **Query** the SQL table to compute aggregations, filters, groupings.
4. **Present** only the summarized results to the user.
"
                },
            });
            sessions[userId] = session;
        }

        var done = new TaskCompletionSource();
        var cancelled = false;
        // Track tool calls: id → (name, startTime)
        var toolTracker = new ConcurrentDictionary<string, (string Name, DateTimeOffset StartTime)>();

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
                    toolTracker[toolId] = (toolStart.Data.ToolName, DateTimeOffset.UtcNow);
                    string? argsJson = null;
                    if (toolStart.Data.Arguments is not null)
                    {
                        try { argsJson = JsonSerializer.Serialize(toolStart.Data.Arguments); } catch { }
                    }
                    sseData = JsonSerializer.Serialize(new { type = "tool_start", tool = toolStart.Data.ToolName, id = toolId, args = argsJson });
                }
                else if (evt is ToolExecutionCompleteEvent toolDone)
                {
                    var toolId = toolDone.Data.ToolCallId ?? "";
                    var toolName = toolTracker.TryGetValue(toolId, out var info) ? info.Name : "unknown";
                    var durationMs = toolTracker.TryGetValue(toolId, out var info2) ? (long)(DateTimeOffset.UtcNow - info2.StartTime).TotalMilliseconds : (long?)null;
                    toolTracker.TryRemove(toolId, out _);
                    string? resultText = null;
                    string? errorText = null;
                    if (toolDone.Data.Result?.Content is not null)
                        resultText = toolDone.Data.Result.Content;
                    else if (toolDone.Data.Result?.DetailedContent is not null)
                        resultText = toolDone.Data.Result.DetailedContent;
                    if (toolDone.Data.Error?.Message is not null)
                        errorText = toolDone.Data.Error.Message;
                    sseData = JsonSerializer.Serialize(new { type = "tool_done", tool = toolName, id = toolId, success = toolDone.Data.Success, durationMs, result = resultText, error = errorText });

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

app.MapGet("/api/models", async (HttpContext ctx) =>
{
    var githubToken = ctx.Session.GetString("github_token");
    if (githubToken is null)
    {
        ctx.Response.StatusCode = 401;
        return;
    }

    var userJson = ctx.Session.GetString("user");
    var user = JsonSerializer.Deserialize<JsonElement>(userJson!);
    var userId = user.GetProperty("id").GetInt64();

    try
    {
        if (clients.TryGetValue(userId, out var client) && client.State == ConnectionState.Connected)
        {
            var models = await client.ListModelsAsync();
            await ctx.Response.WriteAsJsonAsync(models);
        }
        else
        {
            await ctx.Response.WriteAsJsonAsync(Array.Empty<object>());
        }
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
var buildSha = Environment.GetEnvironmentVariable("BUILD_SHA") ?? "dev";
var buildNumber = Environment.GetEnvironmentVariable("BUILD_NUMBER") ?? "0";
app.MapGet("/api/version", () => Results.Ok(new { sha = buildSha, build = buildNumber, started = DateTime.UtcNow.ToString("o") }));

// ──────────────────────────────────────────────
// SPA FALLBACK
// ──────────────────────────────────────────────
app.MapFallbackToFile("index.html");

app.Run();
