# Azure FinOps Agent — Solution Accelerator

An AI-powered agentic solution for **FinOps and InfraOps on Azure**. This solution accelerator serves as both a [Value-Based Delivery (VBD)](https://microsoft.sharepoint.com/sites/VBD) asset for Microsoft Customer Success teams and an open-source reference architecture that customers can clone, deploy, and customize in their own Azure subscription.

## Table of Contents

- [About this repo](#about-this-repo)
- [Key Features](#key-features)
- [Architecture](#architecture)
- [Getting Started](#getting-started)
  - [Prerequisites](#prerequisites)
  - [Products Used](#products-used)
  - [Deploy Instructions](#deploy-instructions)
- [Use Case Scenarios](#use-case-scenarios)
- [Supporting Documentation](#supporting-documentation)
- [Contributing](#contributing)
- [Maintainers](#maintainers)
- [License](#license)
- [Disclaimers](#disclaimers)

## About this repo

Azure FinOps Agent helps Azure customers optimize cloud costs by providing an intelligent agent that sits on top of Microsoft IQ and Microsoft Graph APIs. The agent orchestrates calls to multiple Azure data sources and surfaces actionable FinOps insights through a conversational interface.

### When should you use this repo?

- You want to help customers **identify cost-optimization opportunities** across their Azure estate.
- You need a **repeatable delivery asset** for FinOps and InfraOps customer engagements.
- You want an **AI-powered agent** that can simulate cost scenarios and recommend actions using natural language.
- You are a CSA delivering a **VBD engagement** focused on cloud cost optimization.

## Key Features

- **Simulate potential cost outcomes** — model scenarios and forecast Azure spend under different configurations.
- **Surface immediate cost-optimization actions** — identify low-hanging fruit such as idle resources, oversized VMs, and unattached disks.
- **Provide actionable FinOps insights** — deliver data-driven recommendations aligned with the FinOps Framework.
- **Conversational interface** — ask questions about cost and infrastructure in natural language.
- **Extensible architecture** — plug in additional data sources and tools as needed.

## Architecture

The solution follows an agentic architecture where an AI agent (powered by GitHub Copilot SDK) orchestrates calls to multiple Azure data sources and tools to answer user questions about cloud cost and infrastructure optimization.

| Component     | Technology                                                                                          |
| ------------- | --------------------------------------------------------------------------------------------------- |
| Backend API   | .NET 10 minimal API                                                                                 |
| Frontend      | Vue 3 + Vite SPA with ECharts                                                                       |
| AI Engine     | GitHub Copilot SDK                                                                                  |
| Auth          | GitHub App OAuth (email read-only, no repo access)                                                  |
| Data Sources  | Azure Retail Prices API, Azure Service Health, Microsoft IQ, Microsoft Graph, Azure Cost Management |
| Deployment    | Azure App Service (Linux) via zip push                                                              |
| Custom Domain | https://www.azure-finops-agent.com                                                                  |

<!-- TODO: Add architecture diagram -->

## Getting Started

> **Note:** This project is under active development. Setup instructions will be added as the solution takes shape.

### Prerequisites

- [Azure subscription](https://azure.microsoft.com/free/) with owner access
- [GitHub account](https://github.com) with Copilot access
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) (LTS)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)

### Products Used

- GitHub Copilot SDK
- Azure Cost Management APIs
- Microsoft Graph APIs
- Microsoft IQ
- Azure App Service

### Deploy Instructions

```bash
# 1. Build the Vue frontend
cd src/Dashboard/client
npm install
npm run build

# 2. Run locally (MUST set Development environment)
cd src/Dashboard
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --urls "http://localhost:5000"
# Open http://localhost:5000
```

```powershell
# Deploy to Azure (first time — creates infrastructure)
cd src/Dashboard
.\deploy.ps1 -ResourceGroup "rg-finops-agent" -AppName "finops-agent"

# Subsequent deploys (skip infra)
.\deploy.ps1 -ResourceGroup "rg-finops-agent" -AppName "finops-agent" -SkipInfra
```

The deploy script builds the frontend, publishes .NET with `-r linux-x64` (required for the Copilot SDK native binary), and deploys via `az webapp deploy`.

## Use Case Scenarios

### FinOps Assessment

A Cloud Solution Architect is delivering a VBD engagement to help a customer optimize their Azure spend. The agent surfaces idle resources, oversized VMs, and unattached disks — providing a prioritized list of immediate savings opportunities with estimated cost impact.

### Cost Scenario Modeling

A customer's finance team wants to understand the cost implications of migrating workloads to different VM families or regions. The agent simulates scenarios and forecasts spend under different configurations.

### Ongoing Cost Governance

After the initial engagement, the customer deploys the agent in their own subscription to continuously monitor and optimize cloud costs, receiving actionable recommendations through a conversational interface.

## Supporting Documentation

- [FinOps Framework](https://www.finops.org/framework/)
- [Azure Cost Management documentation](https://learn.microsoft.com/azure/cost-management-billing/)
- [Azure OpenAI Service documentation](https://learn.microsoft.com/azure/ai-services/openai/)
- [Microsoft Graph API documentation](https://learn.microsoft.com/graph/overview)

## Contributing

This project welcomes contributions and suggestions. Please see [CONTRIBUTING.md](CONTRIBUTING.md) for details.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Maintainers

- [Anders Ravnholt](https://github.com/aravnholt) — Sr. Cloud Solution Architect
- [Klaus Gjelstrup Nielsen](https://github.com/klausnielsen) — Sr. Cloud Solution Architect
- [Ali Farahnak](https://github.com/alfarahn) — Senior AI Architect
- [Abishek Narayan](https://github.com/abnaraya) — Cloud Solution Architect

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Disclaimers

This Software requires the use of third-party components which are governed by separate proprietary or open-source licenses as identified below, and you must comply with the terms of each applicable license in order to use the Software. You acknowledge and agree that this license does not grant you a license or other right to use any such third-party proprietary or open-source components.

To the extent that the Software includes components or code used in or derived from Microsoft products or services, including without limitation Microsoft Azure Services (collectively, "Microsoft Products and Services"), you must also comply with the Product Terms applicable to such Microsoft Products and Services. You acknowledge and agree that the license governing the Software does not grant you a license or other right to use Microsoft Products and Services. Nothing in the license or this ReadMe file will serve to supersede, amend, terminate or modify any terms in the Product Terms for any Microsoft Products and Services.

You must also comply with all domestic and international export laws and regulations that apply to the Software, which include restrictions on destinations, end users, and end use. For further information on export restrictions, visit https://aka.ms/exporting.

BY ACCESSING OR USING THE SOFTWARE, YOU ACKNOWLEDGE THAT THE SOFTWARE IS NOT DESIGNED OR INTENDED TO SUPPORT ANY USE IN WHICH A SERVICE INTERRUPTION, DEFECT, ERROR, OR OTHER FAILURE OF THE SOFTWARE COULD RESULT IN THE DEATH OR SERIOUS BODILY INJURY OF ANY PERSON OR IN PHYSICAL OR ENVIRONMENTAL DAMAGE (COLLECTIVELY, "HIGH-RISK USE"), AND THAT YOU WILL ENSURE THAT, IN THE EVENT OF ANY INTERRUPTION, DEFECT, ERROR, OR OTHER FAILURE OF THE SOFTWARE, THE SAFETY OF PEOPLE, PROPERTY, AND THE ENVIRONMENT ARE NOT REDUCED BELOW A LEVEL THAT IS REASONABLY, APPROPRIATE, AND LEGAL, WHETHER IN GENERAL OR IN A SPECIFIC INDUSTRY. BY ACCESSING THE SOFTWARE, YOU FURTHER ACKNOWLEDGE THAT YOUR HIGH-RISK USE OF THE SOFTWARE IS AT YOUR OWN RISK.
