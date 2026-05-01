# Azure FinOps Agent

AI-powered conversational agent that turns Azure cost data into action. Connect your tenant, ask questions in natural language, and get live insights, interactive charts, executive-ready PowerPoint decks, and ready-to-run remediation scripts — what used to take months of FinOps work now takes days.

**[Try it live →](https://azure-finops-agent.com)**

![Azure FinOps Agent — concise FinOps insight on demand](assets/screenshot-finops-shortq.png)

> _Real Q&A from a live tenant: "What's my single biggest cost-saving opportunity in Azure right now?" → in ~30 seconds the agent ran `QueryAzure` + `FindIdleResources`, identified a `Standard_FX8ms_v2` VM in `swedencentral`, and quantified **~$538/month** savings via 3-year Reserved Instance (or **~$434/month** via Savings Plan). Sidebar shows the Crawl maturity score (★★★★★), all four add-on scopes consented (✅), and the Tools panel streams every tool call live._

![Azure FinOps Agent — signed in with all add-on scopes consented](assets/screenshot-loggedin.png)

> _Signed-in shell: the left sidebar exposes the **Crawl / Walk / Run / Playbook** FinOps Foundation maturity categories and a **Subscriptions** browser. The **Add Scopes** panel demonstrates incremental consent — License Optimization, Cost Allocation & Chargeback, Log Analytics Deep Dives, and Cost Exports are each granted as separate delegated, read-only Microsoft Entra consents. Revoke any scope at any time._

![Azure FinOps Agent — world map of current vs upcoming Azure regions](assets/screenshot-worldmap.png)

> _Public-data example (no Azure login required): "Show me all current Azure data centers in one color and upcoming data centers in another color on a world map" — answered with an interactive ECharts world map. The right-hand **Tools** panel streams every tool call (`RenderAdvancedChart`, `web_fetch`, `report_intent`) so you can see exactly what the agent did._

## What It Does

- **Ask anything about your Azure spend** — cost breakdowns, trends, forecasts, anomalies, idle resources, right-sizing opportunities
- **Interactive visualizations** — bar, line, pie, scatter, funnel, world maps, heatmaps, treemaps, radar, and gauge charts rendered inline
- **Generate PowerPoint decks** — executive-ready FinOps presentations with embedded charts, exported as `.pptx`
- **Generate remediation scripts** — downloadable Azure CLI or PowerShell scripts with dry-run mode, confirmation prompts, and `--what-if` safety flags
- **FinOps maturity assessment (Crawl / Walk / Run)** — structured scoring framework aligned with the FinOps Foundation, evaluating tagging, orphaned resources, reservations, budgets, right-sizing, cost allocation, and more — each dimension scored 0–5 with actionable recommendations to level up
- **License optimization** — surface unused M365 seats, Copilot adoption gaps, and license waste across Exchange, Teams, OneDrive, and SharePoint
- **Chargeback & showback** — map costs to departments, teams, and business units using Microsoft Graph directory data
- **Log Analytics deep dives** — KQL queries against workspaces and App Insights for VM metrics, container diagnostics, and ingestion cost analysis

## Architecture

```
┌────────────────────────────────────────────────────────────────────────────┐
│                     Azure App Service (Docker, Linux P0v3)                 │
│                                                                            │
│  ┌────────────────────┐    SSE/POST    ┌────────────────────────────────┐  │
│  │   Vue 3 + Vite     │◄─────────────►│   .NET 10 Minimal API          │  │
│  │   ECharts          │               │   GitHub Copilot SDK (BYOK)    │  │
│  │   App Insights JS  │               │   Azure OpenAI via Entra ID    │  │
│  └────────────────────┘               └───────────────┬────────────────┘  │
│                                                       │ orchestrates       │
│                                             ┌─────────┴──────────┐        │
│                                             │   15 Agent Tools    │        │
│                                             └─────────┬──────────┘        │
│                                                       │                    │
│  ┌─ Auth ──────────────────────────────────────────────┤                   │
│  │  Entra ID OAuth (multi-tenant, incremental consent) │                   │
│  │  5 tiers: ARM · Graph (2×) · Log Analytics · Storage│                   │
│  └─────────────────────────────────────────────────────┘                   │
│                                                                            │
│  ┌─ Observability ─────────────────────────────────────┐                   │
│  │  OpenTelemetry · Azure Monitor · Application Insights│                  │
│  └──────────────────────────────────────────────────────┘                  │
└───────────────────────────────────┬────────────────────────────────────────┘
                                    │
          ┌─────────────────────────┼─────────────────────────┐
          │                         │                         │
 ┌────────▼─────────┐    ┌─────────▼──────────┐    ┌─────────▼──────────┐
 │  Azure ARM APIs  │    │  Microsoft Graph   │    │  Log Analytics     │
 │  ────────────────│    │  ─────────────────  │    │  ─────────────────  │
 │  Cost Management │    │  License inventory │    │  KQL queries       │
 │  Billing         │    │  M365 usage reports│    │  App Insights      │
 │  Advisor         │    │  Directory / Org   │    │  VM & container    │
 │  Resource Graph  │    │  Copilot seat usage│    │    metrics         │
 │  Monitor / VMs   │    │  Intune devices    │    │  Ingestion cost    │
 │  Reservations    │    └────────────────────┘    │    analysis        │
 │  Savings Plans   │                              └────────────────────┘
 │  AKS / Storage   │    ┌────────────────────┐    ┌────────────────────┐
 │  SQL / Cosmos DB  │    │  Azure Retail      │    │  Azure Status      │
 │  App Service     │    │  Prices API        │    │  RSS Feed          │
 │  + 20 more...    │    │  (no auth)         │    │  (no auth)         │
 └──────────────────┘    └────────────────────┘    └────────────────────┘
```

### Tools

| Tool                                  | What it does                                                                                                                 |
| ------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `QueryAzure`                          | ARM REST (GET + read-only POST) — Cost Mgmt, Billing, Advisor, Resource Graph, Monitor, VMs, AKS, Storage, SQL, 30+ services |
| `QueryGraph`                          | Graph GET — license inventory, M365 usage, directory, org chargebacks                                                        |
| `QueryLogAnalytics`                   | KQL against Log Analytics / App Insights                                                                                     |
| `QueryStorage`                        | Read Cost Management export blobs from customer Storage Accounts                                                             |
| `QueryRetailPricing`                  | Public Azure Retail Prices API (no auth) — pricing comparisons & estimates                                                   |
| `FindIdleResources`                   | Detect idle / underutilized VMs, disks, IPs, App Service plans                                                               |
| `DetectCostAnomalies`                 | Spike & anomaly detection across services, scopes, tags                                                                      |
| `SuggestSchedules`                    | Start/stop schedules for dev/test workloads                                                                                  |
| `RenderChart` / `RenderAdvancedChart` | Inline ECharts (bar, line, pie, scatter, funnel, world maps, heatmaps, treemaps, radar, gauge, sankey)                       |
| `GeneratePresentation`                | FinOps PowerPoint decks (python-pptx + matplotlib)                                                                           |
| `GenerateScript`                      | Downloadable Azure CLI / PowerShell remediation scripts                                                                      |
| `ReportMaturityScore`                 | FinOps maturity scoring (Crawl / Walk / Run, 0–5 per dimension)                                                              |
| `GetAzureServiceHealth`               | Azure Status RSS (no auth)                                                                                                   |
| `PublishFAQ`                          | Dynamic SEO pages + IndexNow                                                                                                 |
| `SuggestFollowUp`                     | Clickable follow-up actions                                                                                                  |
| _Built-in (SDK)_                      | bash, Python 3, file ops, web fetch, grep, glob, memory                                                                      |

### Auth & Security

No login required for chat. Azure data via incremental OAuth consent:

| Tier                   | Scopes                                      | Resource          |
| ---------------------- | ------------------------------------------- | ----------------- |
| Connect Azure          | `user_impersonation`                        | Azure ARM         |
| + License Optimization | `Organization.Read.All`, `Reports.Read.All` | Microsoft Graph   |
| + Cost Allocation      | `User.Read.All`, `Group.Read.All`           | Microsoft Graph   |
| + Log Analytics        | `Data.Read`                                 | Log Analytics API |
| + Cost Exports         | `user_impersonation`                        | Azure Storage     |

**Strictly read-only** — PUT/PATCH/DELETE blocked at code level. POST restricted to allowlisted read-only endpoints. Recommended RBAC: `Reader` or `Cost Management Reader`.

## Project Structure

```
src/Dashboard/
├── Program.cs                  # App startup, auth, SSE chat endpoint
├── Auth/
│   └── TokenContext.cs         # Per-user token management
├── AI/Tools/
│   ├── AzureQueryTools.cs      # ARM REST APIs
│   ├── GraphQueryTools.cs      # Microsoft Graph
│   ├── LogAnalyticsQueryTools.cs
│   ├── StorageQueryTools.cs    # Cost Management export blobs
│   ├── RetailPricingTools.cs   # Public Azure Retail Prices (no auth)
│   ├── IdleResourceTools.cs    # Idle / underutilized resource detection
│   ├── AnomalyTools.cs         # Cost spike / anomaly detection
│   ├── ScheduleTools.cs        # Dev/test start-stop schedules
│   ├── ChartTools.cs           # ECharts rendering
│   ├── PresentationTools.cs    # PowerPoint generation
│   ├── ScoreTools.cs           # FinOps maturity scoring
│   ├── ScriptTools.cs          # Remediation scripts
│   ├── HealthTools.cs          # Azure Status RSS
│   ├── FaqTools.cs             # SEO FAQ pages
│   └── FollowUpTools.cs        # Follow-up suggestions
├── Infrastructure/
│   ├── HttpHelper.cs           # HTTP retry + formatting
│   └── TempFileHelper.cs       # Temp file cleanup
├── client/                     # Vue 3 + Vite SPA
│   └── src/components/
│       ├── ChatView.vue        # Chat UI, tool sidebar, ECharts
│       └── Dashboard.vue       # Layout shell
├── Dockerfile                  # Multi-stage (Node → .NET → runtime + Python)
└── setup-entra-app.ps1         # Entra ID app registration
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). This project uses the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).

## License

[MIT](LICENSE)
