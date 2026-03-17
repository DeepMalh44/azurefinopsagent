# Azure FinOps Agent

An AI-powered agent that helps Azure customers **optimize cloud costs** through a conversational interface. Ask questions in natural language and get actionable FinOps insights — from identifying idle resources and oversized VMs to simulating cost scenarios across regions and VM families.

Built with .NET 10, Vue 3, and the GitHub Copilot SDK. Deploys to Azure App Service.

## Getting Started

### Prerequisites

- [Azure subscription](https://azure.microsoft.com/free/) with owner access
- [GitHub account](https://github.com) with Copilot access
- [.NET 10 SDK](https://dotnet.microsoft.com/download), [Node.js LTS](https://nodejs.org/), [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)

### Run Locally

```bash
cd src/Dashboard/client && npm install && npm run build
cd ../
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --urls "http://localhost:5000"
```

### Deploy to Azure

```powershell
cd src/Dashboard
.\deploy.ps1 -ResourceGroup "rg-finops-agent" -AppName "finops-agent"
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).

## Maintainers

- [Anders Ravnholt](https://github.com/aravnholt)
- [Klaus Gjelstrup Nielsen](https://github.com/klausnielsen)
- [Ali Farahnak](https://github.com/alfarahn)
- [Abishek Narayan](https://github.com/abnaraya)

## License

MIT — see [LICENSE](LICENSE).

## Disclaimers

This Software requires the use of third-party components which are governed by separate proprietary or open-source licenses as identified below, and you must comply with the terms of each applicable license in order to use the Software. You acknowledge and agree that this license does not grant you a license or other right to use any such third-party proprietary or open-source components.

To the extent that the Software includes components or code used in or derived from Microsoft products or services, including without limitation Microsoft Azure Services (collectively, "Microsoft Products and Services"), you must also comply with the Product Terms applicable to such Microsoft Products and Services. You acknowledge and agree that the license governing the Software does not grant you a license or other right to use Microsoft Products and Services. Nothing in the license or this ReadMe file will serve to supersede, amend, terminate or modify any terms in the Product Terms for any Microsoft Products and Services.

You must also comply with all domestic and international export laws and regulations that apply to the Software, which include restrictions on destinations, end users, and end use. For further information on export restrictions, visit https://aka.ms/exporting.
