# Azure FinOps Agent

[![Deploy to Azure](https://github.com/Azure-Samples/azure-finops-agent/actions/workflows/deploy.yml/badge.svg)](https://github.com/Azure-Samples/azure-finops-agent/actions/workflows/deploy.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Vue 3](https://img.shields.io/badge/Vue-3-4FC08D?logo=vuedotjs&logoColor=white)](https://vuejs.org/)
[![Live demo](https://img.shields.io/badge/demo-azure--finops--agent.com-0078D4?logo=microsoftazure&logoColor=white)](https://azure-finops-agent.com)

Replace a multi-week FinOps assessment with a single conversation. Connect a tenant or drop a cost export, ask in plain language, and walk away with quantified savings, a FinOps Foundation maturity score, a CFO-ready PowerPoint deck, and ready-to-run remediation scripts — in minutes, not sprints.

The agent can apply fixes directly via Azure REST APIs (`POST` / `PUT` / `PATCH` — e.g. tags, budgets, anomaly alerts, autoshutdown, scheduled actions) using your delegated Entra token, or hand you a downloadable PowerShell / Bash script if you'd rather review and run it yourself. **`DELETE` is blocked at the code level**, so destructive cleanup always stays in human hands. Multi-tenant, safe to point at anything from a dev sandbox to a global enterprise estate.

Built for FinOps leads, CCoE teams, and the architects who serve them.

**[Try it live →](https://azure-finops-agent.com)**

![Azure FinOps Agent — concise FinOps insight on demand](assets/screenshot-finops-shortq.png)

> _Real Q&A from a live tenant: "What's my single biggest cost-saving opportunity in Azure right now?" → in ~30 seconds the agent ran `QueryAzure` + `FindIdleResources`, identified a `Standard_FX8ms_v2` VM in `swedencentral`, and quantified **~$538/month** savings via 3-year Reserved Instance (or **~$434/month** via Savings Plan). Sidebar shows the Crawl maturity score (★★★★★), all four add-on scopes consented (✅), and the Tools panel streams every tool call live._

![Azure FinOps Agent — signed in with all add-on scopes consented](assets/screenshot-loggedin.png)

> _Signed-in shell: the left sidebar exposes the **Crawl / Walk / Run / Playbook** FinOps Foundation maturity categories and a **Subscriptions** browser. The **Add Scopes** panel demonstrates incremental consent — License Optimization, Cost Allocation & Chargeback, Log Analytics Deep Dives, and Cost Exports are each granted as separate delegated, read-only Microsoft Entra consents. Revoke any scope at any time._

![Azure FinOps Agent — world map of current vs upcoming Azure regions](assets/screenshot-worldmap.png)

> _Public-data example (no Azure login required): "Show me all current Azure data centers in one color and upcoming data centers in another color on a world map" — answered with an interactive ECharts world map. The right-hand **Tools** panel streams every tool call (`RenderAdvancedChart`, `web_fetch`, `report_intent`) so you can see exactly what the agent did._

## What It Does

- **Quantified savings, ranked by $ impact** — Reservations, Savings Plans, Hybrid Benefit, rightsizing, idle / orphaned resources, with utilization evidence and an estimated annual saving
- **FinOps Foundation maturity score** — Crawl / Walk / Run grading with 0–5 per capability and a prioritized roadmap; the assessment a consultant bills weeks for, delivered in a chat
- **Agentic reasoning, not a dashboard** — plans the investigation, picks the right ARM / Graph / Log Analytics calls, runs Python (pandas, numpy) mid-conversation, and revises when the data surprises it
- **Anomalies, chargeback & tag hygiene** — catches cost spikes and explains the _why_ (resource, change, owner); auto-generated showback by tag or business unit; quantifies untagged spend
- **M365 license & Copilot ROI** — Microsoft Graph reveals unused licenses, Copilot seat utilization, and SKU mismatch across Exchange, Teams, OneDrive, SharePoint — levers Azure Cost Management can't see
- **Inline charts + CFO-ready PowerPoint** — 20+ ECharts visualizations (bar, line, pie, scatter, funnel, world maps, heatmaps, treemaps, radar, gauge, sankey) plus a one-click branded `.pptx` export
- **Every Azure service, every scope** — 40+ services across all subscriptions and management groups in a single query: compute, AKS, Databricks, Synapse, ML, Cosmos, networking, storage, carbon
- **Drop-in file analysis (no Azure consent required)** — drag any CSV, TSV, JSON, TXT, log, Markdown, XLSX, PDF or Parquet into the chat and click **Analyze**. The agent learns the schema, runs targeted aggregates/filters server-side (pandas/openpyxl/pyarrow/pdfminer), and answers without ever loading the raw payload into the LLM context. Perfect for ad-hoc cost exports, Advisor JSON, audit logs, or executive PDFs. See [`samples/uploads/`](samples/uploads/) for a generator (`generate.py`) and a catalog of realistic test files (~14 MB across 9 file types) you can drop in to try it.
- **Generate remediation scripts** — downloadable Azure CLI / PowerShell with dry-run mode, confirmation prompts, and `--what-if` safety flags
- **Log Analytics deep dives** — KQL against workspaces and App Insights for VM metrics, container diagnostics, and ingestion cost analysis

## Architecture

```
┌────────────────────────────────────────────────────────────────────────────┐
│                     Azure App Service (Docker, Linux P0v3)                 │
│                                                                            │
│  ┌────────────────────┐    SSE/POST    ┌────────────────────────────────┐  │
│  │   Vue 3 + Vite     │◄─────────────►│   .NET 10 Minimal API          │  │
  │   ECharts          │               │   GitHub Copilot SDK 0.3.0     │  │
  │   App Insights JS  │               │   Azure OpenAI via Managed ID  │  │
  └────────────────────┘               └───────────────┬────────────────┘  │
                                                       │ orchestrates       │
                                             ┌─────────┴──────────┐        │
                                             │   20 Agent Tools    │        │
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

| Tool                                              | What it does                                                                                                                                                                                             |
| ------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `QueryAzure`                                      | ARM REST (GET/POST/PUT/PATCH; DELETE blocked) — Cost Mgmt, Billing, Advisor, Resource Graph, Monitor, VMs, AKS, Storage, SQL, 30+ services                                                               |
| `BulkAzureRequest`                                | Fan out N ARM requests in parallel server-side (bulk tagging, autoshutdown rollout, budget rollout) — 1 tool call instead of N                                                                          |
| `QueryGraph`                                      | Graph GET — license inventory, M365 usage, directory, org chargebacks                                                                                                                                    |
| `QueryLogAnalytics`                               | KQL against Log Analytics / App Insights                                                                                                                                                                 |
| `ListCostExportBlobs` / `ReadCostExportBlob`      | Read Cost Management export blobs (FOCUS format) from customer Storage Accounts                                                                                                                          |
| `GetAzureRetailPricing`                           | Public Azure Retail Prices API (no auth) — pricing comparisons & estimates                                                                                                                               |
| `FindIdleResources`                               | Detect idle / underutilized VMs, disks, IPs, App Service plans                                                                                                                                           |
| `DetectCostAnomalies`                             | Spike & anomaly detection across services, scopes, tags                                                                                                                                                  |
| `SaveReportSchedule` / `List…` / `Delete…`        | Persist scheduled FinOps reports (start/stop windows, anomaly digests, dev/test schedules)                                                                                                               |
| `QueryUploadedFile`                               | Inspect files dropped into the chat (CSV/TSV/JSON/TXT/XLSX/PDF/Parquet) — schema, head/slice, filter, aggregate, text_range, json_path. Server-side pandas/pyarrow/pdfminer keeps the LLM context small. |
| `RenderChart` / `RenderAdvancedChart`             | Inline ECharts (bar, line, pie, scatter, funnel, world maps, heatmaps, treemaps, radar, gauge, sankey)                                                                                                   |
| `GeneratePresentation`                            | FinOps PowerPoint decks (python-pptx + matplotlib)                                                                                                                                                       |
| `GenerateScript`                                  | Downloadable Azure CLI / PowerShell remediation scripts                                                                                                                                                  |
| `ReportMaturityScore` / `GetScoreHistory`         | FinOps maturity scoring (Crawl / Walk / Run, 0–5 per dimension) with history                                                                                                                            |
| `GetAzureServiceHealth`                           | Azure Status RSS (no auth)                                                                                                                                                                               |
| `PublishFAQ`                                      | Dynamic SEO pages + IndexNow                                                                                                                                                                             |
| `SuggestFollowUp`                                 | Clickable follow-up actions                                                                                                                                                                              |
| _Built-in (SDK)_                                  | bash, Python 3, file ops, web fetch, grep, glob, memory                                                                                                                                                  |

### Auth & Security

No login required for chat. Azure data via incremental OAuth consent:

| Tier                   | Scopes                                      | Resource          |
| ---------------------- | ------------------------------------------- | ----------------- |
| Connect Azure          | `user_impersonation`                        | Azure ARM         |
| + License Optimization | `Organization.Read.All`, `Reports.Read.All` | Microsoft Graph   |
| + Cost Allocation      | `User.Read.All`, `Group.Read.All`           | Microsoft Graph   |
| + Log Analytics        | `Data.Read`                                 | Log Analytics API |
| + Cost Exports         | `user_impersonation`                        | Azure Storage     |

**Read + write, never delete.** `GET`/`POST`/`PUT`/`PATCH` are allowed so the agent can apply fixes (tags, budgets, anomaly alerts, scheduled actions, autoshutdown, exports). `DELETE` is blocked at the code level — destructive cleanup is delivered as a downloadable script for the user to review and run. The user's Entra RBAC role is the ultimate boundary: assign `Reader` / `Cost Management Reader` for read-only, or `Contributor` / `Cost Management Contributor` to let the agent apply changes.

## Project Structure

```
src/Dashboard/
├── Program.cs                          # ~220-line composition root: DI wiring + middleware
├── Auth/
│   ├── MicrosoftOAuthOptions.cs        # OAuth config + scopes/host helpers
│   ├── SessionTokenStore.cs            # Refresh + lock pool for ARM/Graph/LA/Storage tokens
│   ├── MicrosoftAuthEndpoints.cs       # Multi-tenant OAuth flow + chained admin consent
│   ├── AzureSessionEndpoints.cs        # Azure status / tenants / disconnect / revoke
│   ├── UserStateJanitor.cs             # Background eviction of idle per-user state
│   └── UserTokens.cs                   # Per-user token holder (volatile fields)
├── AI/
│   ├── CopilotSessionFactory.cs        # CopilotClient + Managed-Identity AOAI tokens + tool catalog
│   ├── ChatEndpoints.cs                # SSE chat endpoint + structured marker dispatch
│   └── Tools/                          # 20 AIFunction tools across 16 source files
│       ├── AzureQueryTools.cs          # QueryAzure + BulkAzureRequest
│       ├── GraphQueryTools.cs          # Microsoft Graph
│       ├── LogAnalyticsQueryTools.cs
│       ├── StorageQueryTools.cs        # ListCostExportBlobs + ReadCostExportBlob
│       ├── RetailPricingTools.cs       # GetAzureRetailPricing (no auth)
│       ├── UploadedFileTools.cs        # User-dropped CSV/JSON/XLSX/PDF/Parquet inspection
│       ├── IdleResourceTools.cs / AnomalyTools.cs / ScheduleTools.cs
│       ├── ChartTools.cs               # ECharts rendering (Render + RenderAdvanced)
│       ├── PresentationTools.cs        # PowerPoint generation (python-pptx)
│       ├── ScoreTools.cs / ScriptTools.cs / HealthTools.cs
│       ├── FaqTools.cs / FollowUpTools.cs
│       └── Resources/                  # Embedded Python helpers
│           ├── pptx_generator.py       # PPTX builder (matplotlib + python-pptx)
│           └── file_inspect.py         # Upload preview + query (pandas/pyarrow/pdfminer)
├── Web/
│   ├── MetaEndpoints.cs                # /api/version, /api/config, /api/models
│   ├── DownloadEndpoints.cs            # PPTX + script downloads
│   ├── UploadEndpoints.cs              # File attachments (drop-in analysis)
│   └── SeoEndpoints.cs                 # FAQ pages + sitemap.xml
├── Observability/
│   └── AiTelemetry.cs                  # ActivitySource + Meter + per-user state registry
├── Infrastructure/
│   ├── HttpHelper.cs                   # HTTP retry + read-only method guard (blocks DELETE)
│   └── TempFileHelper.cs               # Temp file cleanup
├── client/                             # Vue 3 + Vite 7 SPA
│   └── src/
│       ├── components/
│       │   ├── ChatView.vue            # Chat UI, tool sidebar, ECharts
│       │   └── Dashboard.vue           # Layout shell
│       └── data/sidebarCategories.js   # FinOps prompt catalog (Pricing + Crawl/Walk/Run/Playbook)
├── Dockerfile                          # Multi-stage (Node 22 → .NET 10 SDK → runtime + Python 3)
└── setup-entra-app.ps1                 # Entra ID app registration

tests/Dashboard.Tests/                  # xUnit tests for the no-DELETE security boundary
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). This project uses the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).

## License

[MIT](LICENSE)
