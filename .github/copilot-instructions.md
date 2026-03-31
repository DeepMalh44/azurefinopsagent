# Azure FinOps Agent - Copilot Instructions

## Pre-Test Checklist

Before asking the user to test OAuth consent flows locally or in production, **always revoke existing consent grants first** so the Microsoft consent screen appears fresh:

```powershell
# Revoke all consent grants for the app registration
$spId = az ad sp show --id "8997774b-06b8-41ab-9870-4acde3dc779e" --query "id" -o tsv 2>$null
$grants = az rest --method GET --url "https://graph.microsoft.com/v1.0/servicePrincipals/$spId/oauth2PermissionGrants" -o json 2>$null | ConvertFrom-Json
foreach ($g in $grants.value) { Write-Host "Deleting grant $($g.id)"; az rest --method DELETE --url "https://graph.microsoft.com/v1.0/oauth2PermissionGrants/$($g.id)" 2>&1 }
Write-Host "All consent grants revoked — user will see fresh consent screens"
```

This ensures the incremental consent flow works as expected and the user sees the real Microsoft Entra ID consent UI for each tier.

always check if you are logged into:
Basic information
Name
Contoso
Tenant ID
51650aad-d085-4ecb-8b07-d7ed4f5355e0
Primary domain
MngEnvMCAP237604.onmicrosoft.com
License
Microsoft Entra ID Free
Users
2
Groups
3
Applications
3
Devices
2
admin@MngEnvMCAP237604.onmicrosoft.com

## Project Purpose

Read all docs here:
https://github.com/github/copilot-sdk/blob/main/docs/getting-started.md

https://github.com/github/copilot-sdk/blob/main/docs/setup/azure-managed-identity.md

https://github.com/github/copilot-sdk/blob/main/dotnet/README.md

Azure FinOps Agent is an AI-powered agentic solution for FinOps and InfraOps on Azure. It serves as a reference architecture and delivery accelerator (VBD) for Microsoft Customer Success teams to help Azure customers optimize cloud costs.

The agent acts as a frontend on top of Azure Cost Management, Billing, ARM REST APIs, and Microsoft Graph APIs to:

- **Simulate potential cost outcomes** — model scenarios and forecast Azure spend under different configurations.
- **Surface immediate cost-optimization actions** — identify low-hanging fruit such as idle resources, oversized VMs, and unattached disks.
- **Provide actionable FinOps insights** — deliver data-driven recommendations aligned with the FinOps Framework.

## Tech Stack

- **Backend**: .NET 10 minimal API (`src/Dashboard/`)
- **Frontend**: Vue 3 + Vite SPA (`src/Dashboard/client/`) with ECharts for data visualization
- **AI**: GitHub Copilot SDK (`GitHub.Copilot.SDK`) with BYOK (Bring Your Own Key) using Azure OpenAI via Entra ID bearer tokens. Sessions managed via `CopilotClient` / `CopilotSession`. Reasoning effort set to `xhigh`. The Copilot CLI provides built-in tools (file operations, bash, grep, glob, web fetch, memory) — custom tools handle Azure-specific APIs.
- **Auth**: Auto-assigned anonymous sessions (no login required for chat); Microsoft Entra ID OAuth (multi-tenant) for Azure ARM, Microsoft Graph, and Log Analytics APIs
- **Data Sources**: Azure Retail Prices API (no auth), Azure Service Health (no auth), Azure Cost Management APIs, Microsoft Graph APIs, Azure Monitor / Log Analytics APIs, ECharts visualization
- **Observability**: OpenTelemetry + Azure Monitor (Application Insights) — structured traces via `ActivitySource("AzureFinOps.AI")` and custom metrics via `Meter("AzureFinOps.AI")` (chat requests, tool calls, errors, token refreshes, session lifecycle, duration histograms). Frontend telemetry in `client/src/main.js` captures page views, failed browser dependencies, uncaught JS errors, unhandled promise rejections, Vue component errors, and CSP violations. Third-party correlation headers are excluded for `cdn.jsdelivr.net` and `js.monitor.azure.com` so browser telemetry does not break public fetches.
- **Deployment**: Azure App Service (Linux, P0v3 Premium) via Docker container image from Azure Container Registry (ACR). Multi-stage Dockerfile bakes Python 3, pip packages (python-pptx, matplotlib, pandas, numpy, lxml), and CLI tools into the image — no runtime install needed. Legacy zip deployment via `deploy.ps1` still supported for the original `finops-agent` app.
- **Container Registry**: Azure Container Registry (`crfinopsagent.azurecr.io`) — Basic SKU, admin credentials, images built via `az acr build`
- **Container App (staging)**: `finops-agent-container.azurewebsites.net` — Docker container on same P0v3 plan, used for testing before swapping to production
- **Custom Domain**: `https://azure-finops-agent.com` (Azure DNS → Azure App Service with managed SSL; `www.` redirects to bare domain via middleware)
- **License**: MIT

## Azure Icon Assets

- `Icons/` and `assets/Icons/` are **gitignored** — they contain the full Azure Architecture icon pack (~1,370 SVGs) downloaded from [Microsoft](https://learn.microsoft.com/azure/architecture/icons/) for local use only.
- `azure-icons-catalog.drawio` is a generated draw.io catalog of all icons (with embedded base64 SVGs) — also gitignored.
- If a README, doc, or diagram needs a specific icon, copy it into `assets/` (which IS versioned, minus its `Icons/` subfolder).

## Project Structure

```
src/Dashboard/
├── Program.cs              # .NET backend: auth (Microsoft Entra ID), SSE chat endpoint, models, version
├── Dashboard.csproj        # .NET 10 project, GitHub.Copilot.SDK + Azure.Identity + Microsoft.Extensions.AI
├── Tools/
│   ├── AzureQueryTools.cs  # QueryAzure — read-only Azure ARM REST API queries (GET + allowlisted POST only)
│   ├── ChartTools.cs       # RenderChart + RenderAdvancedChart — ECharts visualization (bar, line, pie, scatter, funnel, world maps, heatmaps, treemaps, radar, gauge)
│   ├── FaqTools.cs         # PublishFAQ — dynamically publishes useful Q&As as SEO pages, pings IndexNow
│   ├── FollowUpTools.cs    # SuggestFollowUp — suggests clickable follow-up actions in the UI
│   ├── GraphQueryTools.cs  # QueryGraph — calls Microsoft Graph API using user's delegated token
│   ├── HealthTools.cs      # GetAzureServiceHealth — Azure Status RSS feed (no auth required)
│   ├── HttpHelper.cs       # Shared HTTP helper — retry on 429, response formatting, telemetry
│   ├── LogAnalyticsQueryTools.cs # QueryLogAnalytics — runs KQL queries against Log Analytics workspaces or App Insights
│   ├── PresentationTools.cs # GeneratePresentation — generates FinOps PowerPoint (.pptx) using python-pptx + matplotlib
│   └── TokenContext.cs     # UserTokens — per-user mutable token holder with volatile fields for concurrent access
├── Dockerfile              # Multi-stage Docker build (node:22 + dotnet/sdk:10.0 + dotnet/aspnet:10.0 + Python 3)
├── .dockerignore           # Excludes bin/, obj/, node_modules/, wwwroot/ from Docker context
├── client/
│   ├── index.html          # SPA entry point
│   ├── package.json        # Vue 3, Vite, ECharts
│   ├── vite.config.js      # Dev proxy to :5000, builds to ../wwwroot
│   └── src/
│       ├── main.js
│       ├── App.vue          # Always renders Dashboard — handles auth state, login/logout
│       └── components/
│           ├── Dashboard.vue     # Layout shell
│           └── ChatView.vue      # Full chat UI: left sidebar (prompts, APIs, subscriptions), center chat, right sidebar (tool calls), ECharts
├── appsettings.json              # Base config (empty secrets — safe to commit)
├── appsettings.Local.json        # Local dev secrets (GITIGNORED)
├── appsettings.Production.json   # Production secrets (GITIGNORED)
├── .env.example                  # Documents env var pattern for CI/Azure deployment (Microsoft OAuth + Azure OpenAI + App Insights)
├── deploy.ps1                    # PowerShell zip deployment script (az CLI)
├── setup-entra-app.ps1           # Entra ID app registration setup (creates app, permissions, secret)
├── startup.sh                    # App Service startup — installs Python 3, pip packages, jq, sqlite3
├── .gitignore                    # Excludes secrets, node_modules, bin/obj, wwwroot, publish
└── wwwroot/                      # Built Vue frontend (generated by `npm run build`)
```

## Architecture Principles

- The solution follows an **agentic architecture** where the AI agent orchestrates calls to multiple data sources and tools to answer user questions.
- GitHub Copilot SDK manages the AI session lifecycle — a shared `CopilotClient` spawns the CLI process, per-user `CopilotSession` instances maintain conversation history.
- The Copilot SDK provides built-in tools (file operations, bash, grep, glob, web fetch, memory) — custom tools handle Azure-specific APIs and unique UI features.
- Custom tools are registered as `AIFunction` instances via `AIFunctionFactory.Create` and passed to `CopilotSession` via `SessionConfig.Tools`. Shared (stateless) tools are created once; per-user tools (requiring tokens) are created per user via closure over `UserTokens`.
- BYOK authentication: `ClientSecretCredential` generates bearer tokens for `https://cognitiveservices.azure.com/.default`, passed to the Copilot SDK via `ProviderConfig.BearerToken`. Tokens are cached and auto-refreshed before expiry.
- The backend streams responses via **Server-Sent Events (SSE)** using session event subscriptions (`session.On()`) — deltas, tool starts/completions, chart events, errors.
- The Vue frontend consumes SSE and renders streaming text (character-by-character animation), tool call status in the sidebar, intent text next to the AI avatar, and ECharts visualizations inline.
- Data should be retrieved from APIs at runtime — the agent stores dynamic FAQ entries to disk for SEO but no user data.
- **Session management**: `/api/chat/reset` disposes the `CopilotSession`. The next `/api/chat` creates a fresh session via `copilotClient.CreateSessionAsync()`.
- **Exception handling**: Tools do NOT use try/catch internally. The Copilot CLI handles tool errors and returns them to the LLM. `UseAzureMonitor()` auto-instruments all `HttpClient` calls as dependency telemetry in App Insights.
- The solution is designed for **customer deployment** — customers deploy it in their own Azure subscription.

## Read-Only Security Model

This agent is **strictly read-only** — it cannot create, modify, or delete any Azure resources. This is enforced at multiple layers:

### Code-Level Enforcement (AzureQueryTools.cs)

- **HTTP method whitelist**: Only `GET` and `POST` are accepted. `PUT`, `PATCH`, and `DELETE` are rejected with HTTP 400.
- **POST path allowlist**: POST requests are restricted to a static allowlist of known read-only endpoints (`/query`, `/forecast`, `/resources`, `/generateCostDetailsReport`, `/calculatePrice`, etc.). Mutating POST actions (e.g., `/deallocate`, `/start`, `/restart`, `/return`) are blocked with HTTP 403.
- **GraphQueryTools**: GET only — no write methods.
- **LogAnalyticsQueryTools**: POST to query APIs only — KQL read operations.

### OAuth Scopes (Minimal Permissions)

| Tier                 | Scope                                       | Access Level                     | Notes                                                                                     |
| -------------------- | ------------------------------------------- | -------------------------------- | ----------------------------------------------------------------------------------------- |
| Base (ARM)           | `user_impersonation`                        | Delegated — inherits user's RBAC | ARM only offers this scope; read-only is enforced at code level via method/path allowlist |
| License Optimization | `Organization.Read.All`, `Reports.Read.All` | Read-only by scope definition    | Cannot modify org or report data                                                          |
| Cost Allocation      | `User.Read.All`, `Group.Read.All`           | Read-only by scope definition    | Cannot modify users or groups                                                             |
| Log Analytics        | `Data.Read`                                 | Read-only by scope definition    | Cannot modify workspace data                                                              |

**Note on `user_impersonation`**: Azure ARM's only delegated scope is `user_impersonation`, which theoretically allows writes if the user has write RBAC roles. Since ARM does not offer a read-only delegated scope, **the code-level POST allowlist is the primary security boundary**. Customers deploying this agent can further restrict access by assigning the connected user a read-only RBAC role (e.g., `Reader` or `Cost Management Reader`).

### Recommended RBAC for Connected Users

For maximum security, assign one of these roles to users connecting to the agent:

- **Reader** — read-only across all resource types
- **Cost Management Reader** — read-only for cost/billing data specifically
- **Billing Reader** — read-only for billing accounts and invoices

## Tool Development Patterns (Critical Learnings)

When creating tools for the agent:

- **Return raw API JSON** — do NOT manually parse/deserialize API responses. Let the LLM interpret the raw JSON. Manual parsing is fragile and breaks on nullable fields or schema changes.
- **Use `string` parameters** (not `double` or typed values) — the SDK's type coercion can fail. Accept strings and let .NET interpolate them into URLs.
- **Keep tools simple** — fetch data and return it. Add UTC timestamp context if useful. Avoid complex transformations.
- **Example pattern** (proven working):
  ```csharp
  private static async Task<string> GetData(
      [Description("...")] string param1,
      [Description("...")] string param2)
  {
      var url = $"https://api.example.com?p1={param1}&p2={param2}";
      var json = await Http.GetStringAsync(url);
      return $"Current UTC time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\n{json}";
  }
  ```
- **ChartTools** (`RenderChart` / `RenderAdvancedChart`) returns a serialized JSON object with chart config — the frontend detects `tool_done` for `RenderChart` or `RenderAdvancedChart` and emits a separate `chart` SSE event. `RenderAdvancedChart` accepts raw ECharts option JSON for world maps, heatmaps, treemaps, radar, gauge, etc.
- **FaqTools** (`PublishFAQ`) dynamically publishes useful public Q&As as SEO-indexable HTML pages at `/faq/{slug}`. Entries are stored in a JSON file on disk and auto-submitted to IndexNow for Bing indexing. The sitemap at `/sitemap.xml` is dynamically generated to include both static and community FAQ entries.
- **PresentationTools** (`GeneratePresentation`) generates FinOps PowerPoint (.pptx) presentations using python-pptx + matplotlib (Charts are rendered as images via matplotlib and embedded in slides). The LLM passes structured JSON slide data (title, content, chart, two_column, section layouts). Returns a `__PPTX_READY__:{fileId}:{fileName}:{slideCount}` marker. The SSE handler emits a `pptx_ready` event, and the frontend shows a download button. Files are served via `/api/download/pptx/{fileId}` and auto-cleaned after 30 minutes.
- **AzureQueryTools** (`QueryAzure`) is a **read-only** tool that queries Azure ARM REST APIs (GET and allowlisted POST only) using the user's delegated token from `UserTokens.AzureToken`. Returns raw JSON for the LLM to interpret. **Security**: PUT, PATCH, and DELETE methods are rejected at the code level. POST requests are restricted to an allowlist of known read-only endpoints (`/query`, `/forecast`, `/resources`, `/generateCostDetailsReport`, `/generateReservationDetailsReport`, `/calculatePrice`, `/calculateExchange`, `/validatePurchase`, `/carbonEmissionReports`, `/getEntities`, `/summarize`). Mutating POST actions (e.g., `/deallocate`, `/start`, `/restart`, `/return`) are blocked with HTTP 403. Covers Cost Management (queries, forecasts, cost details report, reservation details report, exports, scheduled actions, views), Budgets, Billing, Consumption (pricesheets, reservation summaries/recommendations/transactions, lots, credits, balances, charges), Reservations, Savings Plans, Advisor, Resource Graph, Monitor, Activity Log, Compute/VMs/VMSS, AKS, Network (ExpressRoute, VPN, public IPs, App Gateways, NAT Gateways), Storage, SQL, SQL Managed Instances, App Service, Azure ML (workspaces, compute instances, GPU clusters, endpoints), Databricks (workspaces, pricing tiers), Cosmos DB (accounts, throughput/RU analysis), Redis Cache, Data Factory, Synapse (SQL pools, Spark pools), Container Apps, Resource Health, Defender for Cloud (security assessments, secure scores), RBAC (role assignments), Locks, Quota, Carbon, Policy/PolicyInsights, Management Groups, Tags, Migrate, and Support. Note: Consumption usageDetails/marketplaces are deprecated — prefer Cost Details API (2025-03-01) or Exports. Consumption reservationDetails is deprecated — prefer generateReservationDetailsReport (Microsoft.CostManagement).
- **GraphQueryTools** (`QueryGraph`) is **read-only** — GET only. calls Microsoft Graph API using `TokenContext.GraphToken`. Used for license inventory, M365 usage reports (Exchange, Teams, OneDrive, SharePoint), M365 Copilot seat usage, M365 app-level usage, Intune device management, directory objects, org structure for FinOps chargebacks.
- **LogAnalyticsQueryTools** (`QueryLogAnalytics`) is **read-only** — runs KQL queries (POST to query API only) against Log Analytics workspaces or App Insights using `TokenContext.LogAnalyticsToken`. Used for VM/container metrics, diagnostics, cost attribution (AzureActivity table), and ingestion cost analysis.
- **TokenContext** (`TokenContext.cs`) provides per-user mutable token storage via `UserTokens` — one instance per user in a `ConcurrentDictionary<long, UserTokens>`. Token fields use `volatile` for cross-thread visibility. A `SemaphoreSlim RefreshLock` serializes token refresh operations within a user session. `UserTokens` instances are passed to tool constructors via closure, so tools always read the latest tokens via direct reference.

## Authentication

No login is required to use the chat. Users are auto-assigned an anonymous session identity on first request.

## Microsoft Entra ID OAuth Setup

A single **multi-tenant** Microsoft Entra ID app registration (`Azure FinOps Agent`) is shared between local development and production — both use the same ClientId/ClientSecret. The OAuth flow exchanges tokens for three separate resources:

- **Azure ARM token** (`https://management.azure.com/user_impersonation`) — for Cost Management, Billing, Advisor, Resource Graph, Monitor, etc.
- **Microsoft Graph token** (explicit granular scopes via incremental consent) — for license inventory, M365 usage reports, directory objects, org structure for chargeback
- **Log Analytics token** (`https://api.loganalytics.io/Data.Read`) — for KQL queries against Log Analytics and App Insights

**Security**: Incremental consent — the app requests minimal permissions upfront (ARM only) and adds Graph/Log Analytics scopes only when the user explicitly opts in via separate consent flows. Each addon tier shows a dedicated Microsoft Entra ID consent screen. All token exchanges use explicit scopes (not `.default`) to prevent silent permission creep.

**Consent Tiers** (each triggers a separate Microsoft Entra ID consent screen):

| Tier                     | Scopes                                      | What admin sees                              |
| ------------------------ | ------------------------------------------- | -------------------------------------------- |
| **Base** (Connect Azure) | `user_impersonation`                        | "Access Azure Service Management as you"     |
| **License Optimization** | `Organization.Read.All`, `Reports.Read.All` | "Read organization info, Read usage reports" |
| **Cost Allocation**      | `User.Read.All`, `Group.Read.All`           | "Read all users' profiles, Read all groups"  |
| **Log Analytics**        | `Data.Read`                                 | "Read Log Analytics data"                    |

The flow:

1. User clicks "Connect Azure" → `/auth/microsoft?tier=base` → Microsoft login → ARM token
2. After connecting, sidebar shows opt-in buttons: "+ License Optimization", "+ Cost Allocation", "+ Log Analytics"
3. Each button redirects to `/auth/microsoft?tier=<tier>` → Microsoft consent screen (only for that tier's scopes)
4. Callback exchanges the auth code for tier-specific tokens and stores in session
5. Per-request, tokens are refreshed and set on the user's `UserTokens` instance before tool execution

Config in `appsettings.json`:

```json
"Microsoft": {
    "ClientId": "",
    "ClientSecret": "",
    "TenantId": "common"
}
```

For Azure App Service: `Microsoft__ClientId`, `Microsoft__ClientSecret`, `Microsoft__TenantId`

### Secrets Management

- `appsettings.Local.json` — local dev secrets (gitignored), **only loaded in Development** (`builder.Environment.IsDevelopment()` guard in `Program.cs`)
- `appsettings.Production.json` — production secrets (gitignored)
- `appsettings.json` — base config with empty placeholders (committed)
- For Azure App Service: secrets are set as app settings via `az webapp config appsettings set` (encrypted at rest)
- Environment variable format: `Microsoft__ClientId`, `Microsoft__ClientSecret`, `Microsoft__TenantId`, `AzureOpenAI__Endpoint`, `AzureOpenAI__DeploymentName` (.NET auto-maps `__` to config sections)
- **Critical**: Never load `appsettings.Local.json` unconditionally — it will override production env vars on Azure

## Running Locally

> **CRITICAL**: You **must** set `ASPNETCORE_ENVIRONMENT=Development` before running the backend.
> Without it, ASP.NET Core defaults to Production, which loads `appsettings.Production.json`
> (production OAuth credentials) and skips `appsettings.Local.json`.

```bash
# 0. One-time: Create Entra ID app registration (run from src/Dashboard/)
.\setup-entra-app.ps1
# Copy the output ClientId/ClientSecret into appsettings.Local.json

# 1. Frontend (dev mode with hot reload — optional, only for UI development)
cd src/Dashboard/client
npm install
npm run build          # Build to wwwroot (required for backend to serve)

# 2. Backend (must set Development environment)
cd src/Dashboard
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --project Dashboard.csproj --urls "http://localhost:5000"

# 3. Open http://localhost:5000
```

Prompt shortcuts are available in `.github/prompts/`:

- `debug-local.prompt.md` for non-container local debugging
- `debug-local-docker.prompt.md` for Docker-based local debugging

## Deploying to Azure

### Docker Container Deployment (Recommended)

The app is deployed as a Docker container to Azure App Service via Azure Container Registry (ACR).

```powershell
cd src/Dashboard

# 1. Get build metadata from git
$buildSha = git rev-parse --short HEAD
$buildNumber = git rev-list --count HEAD

# 2. Build & push image to ACR (cloud build — no local Docker needed)
#    Uses --no-logs to avoid Azure CLI Unicode crash on Windows
#    Passes BUILD_SHA and BUILD_NUMBER as build args baked into the image
az acr build --registry crfinopsagent --image finops-agent:latest --platform linux/amd64 --no-logs --build-arg BUILD_SHA=$buildSha --build-arg BUILD_NUMBER=$buildNumber .

# 3. Restart the container app to pull the new image
az webapp restart --name finops-agent-container --resource-group rg-finops-agent
```

**Key points**:

- Multi-stage Dockerfile: node:22 (frontend) → dotnet/sdk:10.0 (build) → dotnet/aspnet:10.0 (runtime + Python 3 + pip packages)
- All Python dependencies (python-pptx, matplotlib, lxml, pandas, numpy) baked into the image
- Build context is ~76 KB (clean `bin/`, `obj/`, `node_modules/` before building, or rely on `.dockerignore`)
- Use `--no-logs` flag to avoid Azure CLI Unicode crash from vite’s `✓` character on Windows
- ACR: `crfinopsagent.azurecr.io` (Basic SKU, admin enabled)
- Container app: `finops-agent-container` on same `ASP-rgfinopsagent-b74f` P0v3 plan
- Container startup timeout: `WEBSITES_CONTAINER_START_TIME_LIMIT=600`

### Legacy Zip Deployment

Still available for the original `finops-agent` app:

```powershell
# First deploy (creates infrastructure)
cd src/Dashboard
.\deploy.ps1 -ResourceGroup "rg-finops-agent" -AppName "finops-agent"

# Subsequent deploys (skip infra)
.\deploy.ps1 -ResourceGroup "rg-finops-agent" -AppName "finops-agent" -SkipInfra
```

The deploy script:

1. Verifies `az login`
2. Creates resource group + App Service plan (P0v3 Linux) + web app (.NET 10 runtime)
3. Reads production secrets from `appsettings.Production.json` and sets as App Service settings (Microsoft OAuth + Azure OpenAI); configures `startup.sh` for Python/tools
4. Builds Vue frontend via `npm ci && npm run build`
5. Publishes .NET backend via `dotnet publish -r linux-x64 --self-contained false`
6. Deploys via `az webapp deploy --type zip`

## Code Conventions

- Use clean, well-structured C# for the .NET backend following Microsoft coding conventions.
- Use modern JavaScript patterns for the frontend (Vue 3 Composition API with `<script setup>`).
- Keep API endpoints RESTful and well-documented.
- Include proper error handling for API calls (rate limiting, authentication failures, etc.) at the **system boundary** (e.g., the `/api/chat` endpoint). Tools themselves should NOT use try/catch — the Copilot CLI and OpenTelemetry handle exception logging centrally.
- All Azure resource interactions should use managed identity where possible.
- Use `.NET 10` APIs — e.g., `KnownIPNetworks` not deprecated `KnownNetworks`.
- Use `CopilotClient` / `CopilotSession` from `GitHub.Copilot.SDK`.

## Key Terminology

- **FinOps**: Cloud financial management discipline combining finance, technology, and business to optimize cloud spend.
- **InfraOps**: Infrastructure operations — managing and optimizing cloud infrastructure.
- **VBD**: Value-Based Delivery — a Microsoft Customer Success engagement model.
- **MCAPS**: Microsoft Customer & Partner Solutions — the Microsoft org that delivers customer success.

## Debugging with App Insights via Azure CLI

When debugging production issues, use `az monitor app-insights query` to run KQL queries directly against Application Insights — no portal needed. The Azure CLI is already authenticated, so this works immediately.

**App Insights App ID**: `89a08d0e-fb6e-4273-8a94-470699c7cfb2`

Common queries:

```powershell
# Recent errors (traces with severityLevel >= 3)
az monitor app-insights query --app "89a08d0e-fb6e-4273-8a94-470699c7cfb2" --analytics-query "traces | where timestamp > ago(1h) and severityLevel >= 3 | order by timestamp desc | take 20 | project timestamp, message, severityLevel"

# Token/auth issues
az monitor app-insights query --app "89a08d0e-fb6e-4273-8a94-470699c7cfb2" --analytics-query "traces | where timestamp > ago(1h) and message contains 'token' | order by timestamp desc | take 10 | project timestamp, message"

# All recent traces
az monitor app-insights query --app "89a08d0e-fb6e-4273-8a94-470699c7cfb2" --analytics-query "traces | where timestamp > ago(30m) | order by timestamp desc | take 50 | project timestamp, message, severityLevel"

# Failed requests
az monitor app-insights query --app "89a08d0e-fb6e-4273-8a94-470699c7cfb2" --analytics-query "requests | where timestamp > ago(1h) and success == false | order by timestamp desc | take 20 | project timestamp, name, resultCode, duration"
```

Always prefer this over downloading log files or tailing logs — it's faster, structured, and queryable.

## Playwright for Portal Operations

When you need to perform actions on Azure portals, GitHub, or the Microsoft Open Source Management portal (e.g., elevating JIT permissions, configuring repo settings, managing team membership), use **Playwright MCP** to navigate and interact with the portal UI directly from VS Code. This is especially useful for:

- Elevating JIT admin permissions on the repo via the Open Source portal.
- Managing GitHub team membership or repo settings.
- Configuring Azure App Service settings, custom domains, or SSL bindings.
- Any portal-based operation that cannot be done via CLI or API.

Always use Playwright when portal interaction is required rather than asking the user to do it manually.

## Custom Domain Setup

The production app is available at `https://azure-finops-agent.com` (canonical) and `https://www.azure-finops-agent.com` (redirects to bare domain).

- **Domain Registrar**: Namecheap (domain registration + WHOIS privacy only)
- **DNS Provider**: Azure DNS (zone: `azure-finops-agent.com` in `rg-finops-agent`)
  - Migrated from Namecheap BasicDNS to Azure DNS for better corporate proxy trust scoring (shared `registrar-servers.com` nameservers are low-trust)
- **Azure DNS Nameservers**: `ns1-04.azure-dns.com`, `ns2-04.azure-dns.net`, `ns3-04.azure-dns.org`, `ns4-04.azure-dns.info`
- **DNS Records** (managed in Azure DNS zone):
  - `A` `@` → `52.228.84.33` (App Service IP)
  - `CNAME` `www` → `finops-agent-container.azurewebsites.net`
  - `TXT` `asuid` → Azure domain verification token
  - `TXT` `_dmarc` → `v=DMARC1; p=none; rua=mailto:alifarahnak@gmail.com` (domain reputation)
- **Security Headers** (in `Program.cs`): HSTS (1 year, preload), X-Content-Type-Options, X-Frame-Options, Referrer-Policy, Permissions-Policy, Content-Security-Policy, HTTPS redirection
- **SSL**: Azure Managed Certificates (auto-renewed) bound via SNI for both `www` and root domain
- **Microsoft Entra ID callbacks**: `https://azure-finops-agent.com/auth/microsoft/callback`, `https://www.azure-finops-agent.com/auth/microsoft/callback`, `http://localhost:5000/auth/microsoft/callback`, `https://finops-agent-container.azurewebsites.net/auth/microsoft/callback`

## Self-Maintenance

Whenever code changes, new files, or documentation updates are made in this repository, **always update this instructions file** to reflect the current state of the project. This includes:

- New or removed components, services, or folders.
- Changes to the tech stack or architecture.
- New conventions, patterns, or dependencies introduced.
- Updates to team members or ownership.

This file must always be kept up to date so that Copilot has accurate context about the project.

## Repository Context

- This repo lives in the `Azure-Samples` GitHub organization.
- It is an open-source sample/accelerator and VBD delivery asset, not a Microsoft product.
- It follows the same README/repo structure as other Azure-Samples solution accelerators (e.g., `chat-with-your-data-solution-accelerator`, `azure-search-openai-demo`).
- Standard files: `README.md`, `CONTRIBUTING.md`, `SECURITY.md`, `SUPPORT.md`, `CODE_OF_CONDUCT.md`, `LICENSE`.
- Direct owners: Anders Ravnholt, Klaus Gjelstrup Nielsen, Ali Farahnak, Abishek Narayan.

## Future Improvements

### SEO & Community FAQ Growth

- The `PublishFAQ` tool dynamically creates SEO pages from useful public Q&As. Over time, the `/faq` section grows organically as users ask FinOps questions, creating a knowledge base that drives organic search traffic.
- Consider adding a moderation layer to review dynamically published FAQs before they go live.

I work for Microsoft, Ali Rexa Farahnak, and I am a Cloud Solution Architect within the AI and Apps domain and I work for Customer Success organization in Microsoft Denmark, I am participating in a cup I want you to help me win, this is the challenge:
Skip to main content

Microsoft
SharePoint
Search this site

AFAF

Microsoft

Discover

Publish

Build

OneDrive

New SharePoint

EMEA AI Innovation
HomeEMEA Dragon's DenEMEA AI Agent Creation PlaybookAI Transformation (AIT)Related EMEA programs

General
EMEA Dragons Den AI Agent Challenge
EMEA Dragon’s Den AI Agent Challenge
📍 10 OUs 📍 3 Finalist across OUs 📍 1 EMEA Champion

🚀 1. What’s the mission?

We’re kicking off a region-wide challenge to accelerate AI Agent usage and shine a spotlight on the amazing talent across EMEA.

Our objectives:

Build mindshare on the use of Agents across the field.

Encourage usage of the Frontier Cup agents.

Showcase the creativity and impact of EMEA teams in AI Agent creation.

Inspire daily adoption of Agents as Customer Zero.

🟣 2. Campaign premise

The competition will run as an individual competition and unfold as follows:

Each OU will run its own local competition, showcasing AI Agents created within the OU. The local Champ Lead and V-team will select the ultimate OU representative.

All submissions will be reviewed by the OU Champ Community and LT. Note: 1 submission per person.

The top Agent from each OU will advance to the EMEA semi final review board, where the Top 3 Finalists will be selected.

These Top 3 finalists will be invited to a surprise location, where they’ll pitch their AI Agent to the Dragons (selected members of the EMEA LT) in a 6- minute pitch.

One winner will be crowned the EMEA AI Agent Champion, with their story showcased to the entire EMEA community in June.

What is not allowed: No direct copies or re-branding of existing wide agents and / or already scaled use cases.

          Compliance: submissions must adhere to the principles of Responsible & Trusted AI!

📅 3. How to submit your agent and by when

Submit your AI Agent as a one-slide submission, including your name and alias, OU, a link to the agent and your pitch (video, text, audio, be creative!). Please send your submission to your OU mailbox listed below by the April 24th deadline.

Europe North

Europe South

France

Germany & Austria

MEA

Netherlands

SDP

Switzerland

United Kingdom & Ireland

EMEA SE&O

Key Dates

March 2nd: EMEA Enablement Session – Build Agents with Ease (L100) - Please Click here to join (or check the OneEMEA Learning Calendar).

March 2nd - April 24th: OU auditions open

April 24th EOD: OU auditions close

April 27th - May 4th: OU Top nominations review by local Champ Leads and local LT

May 6th: OUs submit their Top 10 nominations

May 11th - May 15th (final date TBC): EMEA semi final: Top 3 finalists selected

May 18th: Top 3 winners announced

May 18th - June 3rd: Top 3 winners can book their trip (secret location)!

Early June (final date TBC): Top 3 winners pitch their Agent: 6-min pitch - 1 EMEA Champion crowned

🏆 4. How will Agents be judged? - The 2 Rs

REAL

Does it deliver tangible customer or business impact?

What specific customer pain point or internal challenge does it address?

Was the Agent used in a new or innovative way?

Are there clear "Aha!" moments that will capture the Dragons attention?

REMARKABLE

Is this a story compelling enough to make the Dragons want to invest and help it scale further?

Did the Agent change or change the "status quo"? (e.g., from a 3-week process to a 3-minute task).

Did it increase pipeline? Shorten the sales cycle by X$ or X% or save X hours of manual work?

Can this win be replicated by across EMEA, or was it a "one-hit wonder"?

🐉 DRAGONS SCORING METHOD

Category

What the Dragons Should Look For...

Problem & Value

Is the problem clearly defined? Does the agent solve a real, meaningful business or customer problem? Is the value obvious?

Impact & Outcomes

If scaled, would this agent save time, reduce cost, improve quality, or unlock growth? Is the impact measurable or believable?

Innovation & Creativity

Is this more than a simple automation? Does it show creative use of AI agents, reasoning, orchestration, or autonomy?

User Experience (UX)

Is it easy to use and intuitive? Would a non‑technical user feel confident using it?

Feasibility & Scalability

Can this realistically be deployed and scaled across teams, markets, or customers?

Responsible & Trusted AI

Does it respect data security, privacy, compliance, and responsible AI principles?

Clarity of Pitch

Was the idea clearly explained? Did the team articulate what it does, why it matters, and how it works?

Bonus – WOW Factor: Did it genuinely surprise or excite you? Would you champion this idea?

Bonus (if tie breaker) – Weekly Leaderboard

🥇 1st place OU: +3 points

🥈 2nd place OU: +2 points

🥉 3rd place OU: +1 point

See all

MAR-APR
2-24
Dragons Den Competition
EMEA Dragon's Den submissions
Mon 2 Mar, All day
APR-MAY
27-4
Dragons Den Competition
OU Top nominations review by local Champ Leads and local LT
Mon 27 Apr, All day
MAY
6
Dragons Den Competition
OUs submit their Top 10 nominations
Wed 6 May, All day
MAY
11-15
Dragons Den Competition
EMEA semi final: Top 3 finalists selection
Mon 11 May, All day

