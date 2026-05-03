using Azure.Core;
using Azure.Identity;
using AzureFinOps.Dashboard.AI.Tools;
using AzureFinOps.Dashboard.Auth;
using AzureFinOps.Dashboard.Observability;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.AI;

/// <summary>
/// Owns the shared <see cref="CopilotClient"/>, BYOK bearer token cache, the
/// catalog of stateless tools, and the per-user tool list (which captures each
/// user's <see cref="UserTokens"/> via closure).
/// </summary>
public sealed class CopilotSessionFactory : IAsyncDisposable
{
    public const string SystemPrompt = @"
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
- When the user has dropped files into the chat, an [UPLOADED FILES IN THIS SESSION ...] block is injected at the top of their message listing each fileId, kind, and size. Use **QueryUploadedFile(fileId, mode, paramsJson)** to inspect them — start with `mode='preview'` to learn the shape, then narrow with `head` / `slice` / `filter` / `aggregate` / `text_range` / `json_path`. Each call is capped at ~200 rows or ~8000 chars; issue more calls if needed. The user's question is almost certainly about the file they just dropped — answer it from the file rather than asking them to paste data.
- Wait for tool results before rendering charts — never render with empty data.
- Call independent tools in parallel (e.g. QueryAzure + QueryGraph simultaneously).
- After answering a public FinOps question, call PublishFAQ to save it as an SEO page. Never publish tenant-specific data.
- After every answer, call SuggestFollowUp with the single most useful FinOps next step **derived from the data the user just saw and the prior conversation** — never generic. Examples: after a service breakdown → drill into the top-spending service by name; after listing idle disks → generate a cleanup script for those specific disks; after a cost trend → forecast the rest of the month; after a maturity score → the next-level scoring prompt. Keep the label ≤60 chars. The follow-up MUST reference a concrete entity (resource name, service, RG, subscription, tier, region, time window) from this turn — no vague suggestions like ""explore costs"" or ""tell me more"". Skip ONLY when the conversation has clearly reached a natural endpoint.
- **Uploaded-file follow-ups must be sharper.** When the user dropped files and you just analyzed them, the follow-up MUST propose the single highest-leverage *action* they can take on their own data — not another analytical question. Good: ""Generate a cleanup script for the 47 unattached disks in rg-data-eus2"", ""Rank the top 5 prioritized actions across all uploads"", ""Build the CFO deck from these files"", ""Tag the 312 untagged resources via PATCH"". Bad: ""Want more details?"", ""Show me the data again"". When ≥3 files were uploaded, prefer follow-ups that cut across multiple files (cost × inventory, Advisor × cost, etc.) and produce a deliverable the user can take to a meeting (script, deck, ranked action list).

## Speed (treat latency as a first-class concern)
Every avoidable round-trip costs the user 1-3s. Apply these without being asked:

1. **Parallelize aggressively.** When you need data from N sources that don't depend on each other, issue ALL N tool calls in the same response — do NOT await one before starting the next. Examples: cost query + Advisor recommendations + budget list = 3 parallel calls, not 3 sequential ones. Resource Graph queries across different subscriptions = parallel. Pricing lookups for multiple SKUs = parallel.
2. **Prefer Resource Graph over per-resource list APIs.** One KQL query against `/providers/Microsoft.ResourceGraph/resources` returns inventory across all subscriptions in a single ~500ms call. The list endpoints (e.g. `/subscriptions/{id}/providers/Microsoft.Compute/virtualMachines`) require one call PER subscription PER resource type — avoid them for cross-cutting questions.
3. **Aggregate at source, never client-side.** Cost Management `/query` with `groupBy=ServiceName` returns 10 rows; querying raw and grouping yourself returns 10,000. Always push grouping/filtering/$top into the query body.
4. **Project narrow columns.** In Resource Graph KQL always use `project name, type, location, tags` — never select everything. In Cost Management always specify `dataset.aggregation` instead of returning all metrics.
5. **Reuse data within a turn.** If you already fetched the subscription list at the top of the turn, do not re-fetch it for the next sub-question. The conversation history is your cache.
6. **Skip confirmation round-trips.** When the user asks for an action with clear intent (""apply tag X to all untagged"", ""score my Crawl maturity""), execute immediately. Do NOT ask ""shall I proceed?"" — that doubles the perceived latency. Only confirm when the action would have material cost (>$1k/mo) or wide blast radius (touches >100 resources).
7. **Bound list sizes.** Default `top=20` for Resource Graph, `$top=50` for Advisor, `top=10` for cost queries unless the user explicitly asks for more. Truncated answers are fast and let the user drill down via SuggestFollowUp.
8. **One chart OR one table per response.** Rendering both doubles the LLM output tokens. Pick the better fit.

## Large Data Strategy
APIs can return massive payloads. Follow this hierarchy:
1. **Scope at the source** — each tool description tells you how to filter, group, and limit. ALWAYS aggregate in the query itself (grouping, summarize, $top, $select). Never request raw ungrouped data.
2. **Python post-processing** — when a response is still large or needs transformation (pivoting, derived metrics, multi-source joins), save the JSON to a file and run a Python script with pandas/numpy to process it. Don't try to reason over 100KB+ of raw JSON.
3. **Drill-down pattern** — start with a high-level aggregated query to understand the shape, then drill into the top items with targeted queries.

## Mutations Are Allowed (Read + Write, Never Delete)
You CAN modify Azure resources via PUT/PATCH/POST when the user asks (e.g. apply tags, create budgets, set anomaly alerts, configure scheduled actions, set autoshutdown, enable cost exports). Only DELETE is blocked at the code level — the agent never deletes resources. For destructive cleanup (removing idle disks, orphaned IPs, expired snapshots), call **GenerateScript** so the user can review and run it themselves.

Do NOT refuse a mutation on grounds of ""governance hygiene"" or ""best practices"" — the user is the operator and owns those decisions. Acknowledge the request and execute. The only refusals you should issue are: (a) genuinely destructive deletes (those are blocked anyway), (b) credential exfiltration, or (c) requests that would cost the user >$1,000/month without explicit confirmation of the dollar impact.

## Bulk Tagging — Efficient Pattern
Tagging 100+ resources is the most common bulk operation. Use this exact recipe to avoid 100+ sequential round-trips:

1. **Discover targets in ONE call** via Resource Graph:
   `POST /providers/Microsoft.ResourceGraph/resources?api-version=2024-04-01`
   body: `{""query"":""Resources | where isnull(tags['cost-center']) | project id, name, tags | limit 200"", ""subscriptions"":[""<subId>""]}`
2. **Apply tags via the dedicated tags endpoint** (preserves other tags):
   `PATCH {resourceId}/providers/Microsoft.Resources/tags/default?api-version=2021-04-01`
   body: `{""operation"":""Merge"",""properties"":{""tags"":{""cost-center"":""eng"",""environment"":""prod""}}}`
   The `Merge` operation only adds/updates — it does NOT delete existing tags. Use `Replace` for full overwrite, `Delete` to remove specific tag keys.
3. **Issue PATCH calls in parallel** — call QueryAzure multiple times in the same turn for different resources; the runtime will execute them concurrently. Batch in waves of ~20 to stay well below ARM throttling (1200 writes/hour per subscription).
4. **Report a single summary** — ""Tagged 47/50 resources (3 failed: <names>)"". Do NOT echo every individual response.

For a quick demo bad→good story: first run the Resource Graph discovery (shows N untagged resources, low score), then apply tags with the bulk pattern above (shows N→0 untagged, score jumps).
";

    private static readonly TokenRequestContext CognitiveServicesScope =
        new(new[] { "https://cognitiveservices.azure.com/.default" });

    private readonly AiTelemetry _telemetry;
    private readonly CopilotClient _copilotClient;
    private readonly ClientSecretCredential _credential;
    private readonly string _endpoint;
    private readonly string _deployment;
    private readonly List<AIFunction> _sharedTools;
    private readonly ILogger _logger;

    private readonly SemaphoreSlim _bearerTokenLock = new(1, 1);
    private string? _cachedBearerToken;
    private DateTimeOffset _bearerTokenExpiry = DateTimeOffset.MinValue;

    public string Deployment => _deployment;

    private CopilotSessionFactory(
        AiTelemetry telemetry,
        CopilotClient copilotClient,
        ClientSecretCredential credential,
        string endpoint,
        string deployment,
        List<AIFunction> sharedTools,
        ILogger logger)
    {
        _telemetry = telemetry;
        _copilotClient = copilotClient;
        _credential = credential;
        _endpoint = endpoint;
        _deployment = deployment;
        _sharedTools = sharedTools;
        _logger = logger;
    }

    public static async Task<CopilotSessionFactory> CreateAsync(
        AiTelemetry telemetry,
        MicrosoftOAuthOptions oauthOptions,
        string azureOpenAIEndpoint,
        string azureOpenAIDeployment,
        ILoggerFactory loggerFactory)
    {
        var copilotClient = new CopilotClient();
        await copilotClient.StartAsync();

        var credential = new ClientSecretCredential(
            oauthOptions.HomeTenantId, oauthOptions.ClientId, oauthOptions.ClientSecret);

        var chartLogger = loggerFactory.CreateLogger("AzureFinOps.AI.Charts");
        var sharedTools = new List<AIFunction>();
        sharedTools.AddRange(ChartTools.Create(chartLogger));
        sharedTools.AddRange(HealthTools.Create());
        sharedTools.AddRange(PresentationTools.Create());
        sharedTools.AddRange(FollowUpTools.Create());
        sharedTools.AddRange(FaqTools.Create());
        sharedTools.AddRange(ScoreTools.Create());
        sharedTools.AddRange(ScriptTools.Create());
        sharedTools.AddRange(ScheduleTools.Create());
        sharedTools.AddRange(RetailPricingTools.Create());

        var logger = loggerFactory.CreateLogger("AzureFinOps.AI");
        logger.LogInformation("CopilotClient started; Azure OpenAI BYOK endpoint={Endpoint} deployment={Deployment}",
            azureOpenAIEndpoint, azureOpenAIDeployment);

        return new CopilotSessionFactory(telemetry, copilotClient, credential,
            azureOpenAIEndpoint, azureOpenAIDeployment, sharedTools, logger);
    }

    public List<AIFunction> GetOrCreateUserTools(long userId)
    {
        return _telemetry.UserTools.GetOrAdd(userId, uid =>
        {
            var tokens = _telemetry.UserTokens.GetOrAdd(uid, id => new UserTokens { UserId = id });
            var tools = new List<AIFunction>(_sharedTools);
            tools.AddRange(new AzureQueryTools(tokens).Create());
            tools.AddRange(new GraphQueryTools(tokens).Create());
            tools.AddRange(new LogAnalyticsQueryTools(tokens).Create());
            tools.AddRange(new StorageQueryTools(tokens).Create());
            tools.AddRange(new AnomalyTools(tokens).Create());
            tools.AddRange(new IdleResourceTools(tokens).Create());
            tools.AddRange(new UploadedFileTools(tokens).Create());
            return tools;
        });
    }

    public async Task<CopilotSession> GetOrCreateSessionAsync(long userId, string userLogin)
    {
        if (_telemetry.UserSessions.TryGetValue(userId, out var existing))
            return existing;

        var config = await CreateSessionConfigAsync(userId);
        var session = await _copilotClient.CreateSessionAsync(config);
        _telemetry.UserSessions[userId] = session;
        _telemetry.ActiveSessions.Add(1);
        _logger.LogInformation("Created new Copilot session for {User} sessionId={SessionId}", userLogin, session.SessionId);
        return session;
    }

    public async Task<CopilotSession> RecreateSessionAsync(long userId, string userLogin)
    {
        if (_telemetry.UserSessions.TryRemove(userId, out _))
            _telemetry.ActiveSessions.Add(-1);

        var config = await CreateSessionConfigAsync(userId);
        var session = await _copilotClient.CreateSessionAsync(config);
        _telemetry.UserSessions[userId] = session;
        _telemetry.ActiveSessions.Add(1);
        _logger.LogInformation("Recreated Copilot session for {User} sessionId={SessionId}", userLogin, session.SessionId);
        return session;
    }

    private async Task<SessionConfig> CreateSessionConfigAsync(long userId)
    {
        var bearerToken = await GetAzureOpenAIBearerTokenAsync();
        return new SessionConfig
        {
            Model = _deployment,
            ReasoningEffort = "xhigh",
            Streaming = true,
            Tools = GetOrCreateUserTools(userId),
            OnPermissionRequest = (_, _) => Task.FromResult(new PermissionRequestResult { Kind = PermissionRequestResultKind.Approved }),
            Provider = new ProviderConfig
            {
                Type = "azure",
                BaseUrl = _endpoint.TrimEnd('/'),
                BearerToken = bearerToken,
            },
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = SystemPrompt,
            },
        };
    }

    private async Task<string> GetAzureOpenAIBearerTokenAsync()
    {
        if (_cachedBearerToken is not null && _bearerTokenExpiry > DateTimeOffset.UtcNow.AddMinutes(5))
            return _cachedBearerToken;

        await _bearerTokenLock.WaitAsync();
        try
        {
            if (_cachedBearerToken is not null && _bearerTokenExpiry > DateTimeOffset.UtcNow.AddMinutes(5))
                return _cachedBearerToken;

            var tokenResult = await _credential.GetTokenAsync(CognitiveServicesScope, CancellationToken.None);
            _cachedBearerToken = tokenResult.Token;
            _bearerTokenExpiry = tokenResult.ExpiresOn;
            _logger.LogInformation("Azure OpenAI bearer token refreshed, expires at {Expiry}", _bearerTokenExpiry);
            return _cachedBearerToken;
        }
        finally
        {
            _bearerTokenLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await _copilotClient.DisposeAsync(); } catch { }
        _bearerTokenLock.Dispose();
    }
}
