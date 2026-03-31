# Azure FinOps Agent

An AI-powered conversational agent for Azure FinOps and InfraOps. Surface savings, forecast spend, query live tenant data, and generate executive-ready PowerPoint decks — from data to decision in minutes, not weeks.

**[Try it live →](https://azure-finops-agent.com)**

![Azure FinOps Agent](assets/screenshot.jpg)

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          Azure App Service                              │
│                                                                         │
│  ┌───────────────┐   SSE/POST   ┌────────────────────────────────────┐  │
│  │  Vue 3 SPA    │◄────────────►│  .NET 10 Minimal API               │  │
│  │  ECharts      │              │  GitHub Copilot SDK (BYOK)         │  │
│  │  streaming UI │              │  Azure OpenAI                      │  │
│  └───────────────┘              └──────────┬─────────────────────────┘  │
│                                            │ orchestrates               │
│                                  ┌─────────┴─────────┐                  │
│                                  │    Agent Tools     │                  │
│                                  └─────────┬─────────┘                  │
└────────────────────────────────────────────┼────────────────────────────┘
                                             │
                 ┌───────────────────────────┼───────────────────────────┐
                 │                           │                           │
        ┌────────▼────────┐       ┌──────────▼──────────┐    ┌──────────▼──────────┐
        │  Azure ARM APIs │       │  Microsoft Graph    │    │  Log Analytics      │
        │  Cost Mgmt      │       │  Licenses, M365     │    │  KQL queries        │
        │  Billing        │       │  Directory, Org     │    │  App Insights       │
        │  Advisor        │       │  Usage reports      │    │  VM/container data  │
        │  Resource Graph │       └─────────────────────┘    └─────────────────────┘
        │  Monitor, VMs   │
        │  Reservations   │       ┌─────────────────────┐    ┌─────────────────────┐
        │  Savings Plans  │       │  Azure Retail       │    │  Azure Status       │
        │  + 30 more...   │       │  Prices API         │    │  RSS Feed           │
        └─────────────────┘       │  (no auth)          │    │  (no auth)          │
                                  └─────────────────────┘    └─────────────────────┘
```

The agent follows an **agentic architecture** — the LLM autonomously orchestrates tool calls across Azure APIs, Graph, and Log Analytics to answer user questions. Responses stream via SSE with inline ECharts visualizations, tool call status in a sidebar, and downloadable PowerPoint decks.

### Tech Stack

| Layer             | Technology                                                   |
| ----------------- | ------------------------------------------------------------ |
| **Frontend**      | Vue 3 + Vite, ECharts                                        |
| **Backend**       | .NET 10 minimal API                                          |
| **AI**            | GitHub Copilot SDK, Azure OpenAI (BYOK via Entra ID)         |
| **Auth**          | Microsoft Entra ID OAuth (multi-tenant, incremental consent) |
| **Observability** | OpenTelemetry + Azure Monitor / Application Insights         |
| **Deployment**    | Docker → ACR → Azure App Service (Linux P0v3)                |

### Agent Tools

| Tool                    | Purpose                                                                                                                                                               |
| ----------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `QueryAzure`            | Any Azure ARM REST API (GET/POST) — Cost Management, Billing, Advisor, Resource Graph, Monitor, Reservations, Savings Plans, VMs, AKS, Storage, SQL, and 30+ services |
| `QueryGraph`            | Microsoft Graph — license inventory, M365 usage reports, directory objects, org structure for chargebacks                                                             |
| `QueryLogAnalytics`     | KQL queries against Log Analytics workspaces and App Insights                                                                                                         |
| `RenderChart`           | ECharts visualizations inline (bar, line, pie, scatter, funnel, world maps, heatmaps, treemaps, radar, gauge)                                                         |
| `GeneratePresentation`  | FinOps PowerPoint decks via python-pptx + matplotlib                                                                                                                  |
| `GetAzureServiceHealth` | Azure Status RSS feed (no auth)                                                                                                                                       |
| `PublishFAQ`            | Dynamic SEO FAQ pages with IndexNow                                                                                                                                   |
| `SuggestFollowUp`       | Clickable follow-up action buttons                                                                                                                                    |
| _Built-in (SDK)_        | bash, Python 3, file ops, web fetch, grep, glob, memory                                                                                                               |

### Auth Model

No login required for chat. Users connect Azure data via incremental OAuth consent:

1. **Connect Azure** → ARM token (`user_impersonation`)
2. **+ License Optimization** → Graph token (`Organization.Read.All`, `Reports.Read.All`)
3. **+ Cost Allocation** → Graph token (`User.Read.All`, `Group.Read.All`)
4. **+ Log Analytics** → Log Analytics token (`Data.Read`)

Each tier shows a dedicated Microsoft Entra ID consent screen — minimal permissions upfront.

## Getting Started

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download), [Node.js LTS](https://nodejs.org/), Microsoft Entra ID app registration

```bash
# Build frontend & start backend
cd src/Dashboard/client && npm install && npm run build && cd ..
ASPNETCORE_ENVIRONMENT=Development dotnet run --urls "http://localhost:5000"
```

> PowerShell: `$env:ASPNETCORE_ENVIRONMENT="Development"` before `dotnet run`

### Deploy

**Docker (recommended):**

```powershell
cd src/Dashboard
az acr build --registry <your-acr> --image finops-agent:latest --platform linux/amd64 .
az webapp restart --name <your-app> --resource-group <your-rg>
```

**Zip deploy:**

```powershell
.\deploy.ps1 -ResourceGroup "rg-finops-agent" -AppName "finops-agent"
```

## Project Structure

```
src/Dashboard/
├── Program.cs              # Auth, SSE chat endpoint, middleware
├── Tools/                  # Agent tool implementations
│   ├── AzureQueryTools.cs  # ARM REST APIs (delegated token)
│   ├── GraphQueryTools.cs  # Microsoft Graph (delegated token)
│   ├── LogAnalyticsQueryTools.cs  # KQL (delegated token)
│   ├── ChartTools.cs       # ECharts rendering
│   ├── PresentationTools.cs  # PowerPoint generation
│   ├── HealthTools.cs      # Azure Status RSS
│   ├── FaqTools.cs         # SEO FAQ pages
│   ├── FollowUpTools.cs    # Follow-up suggestions
│   ├── HttpHelper.cs       # Shared HTTP helper (retry, formatting)
│   └── TokenContext.cs     # Per-user token management
├── client/                 # Vue 3 + Vite SPA
│   └── src/components/
│       ├── ChatView.vue    # Chat UI, tool sidebar, ECharts
│       └── Dashboard.vue   # Layout shell
├── Dockerfile              # Multi-stage (Node → .NET → runtime + Python)
└── deploy.ps1              # Zip deployment script
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). This project uses the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).

## License

[MIT](LICENSE)

## Disclaimers

This Software requires the use of third-party components which are governed by separate proprietary or open-source licenses as identified below, and you must comply with the terms of each applicable license in order to use the Software. You acknowledge and agree that this license does not grant you a license or other right to use any such third-party proprietary or open-source components.

To the extent that the Software includes components or code used in or derived from Microsoft products or services, including without limitation Microsoft Azure Services (collectively, "Microsoft Products and Services"), you must also comply with the Product Terms applicable to such Microsoft Products and Services. You acknowledge and agree that the license governing the Software does not grant you a license or other right to use Microsoft Products and Services. Nothing in the license or this ReadMe file will serve to supersede, amend, terminate or modify any terms in the Product Terms for any Microsoft Products and Services.

You must also comply with all domestic and international export laws and regulations that apply to the Software, which include restrictions on destinations, end users, and end use. For further information on export restrictions, visit https://aka.ms/exporting.
