# Contributing to Azure FinOps Agent

This project welcomes contributions and suggestions. Most contributions require you to agree to a Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us the rights to use your contribution. For details, visit https://cla.opensource.microsoft.com.

When you submit a pull request, a CLA bot will automatically determine whether you need to provide a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## How to Contribute

1. Fork this repository.
2. Create a feature branch (`git checkout -b feature/my-feature`).
3. Commit your changes (`git commit -am 'Add my feature'`).
4. Push to the branch (`git push origin feature/my-feature`).
5. Open a Pull Request.

## Development Setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js LTS](https://nodejs.org/)
- GitHub App OAuth credentials (for local dev, see Secrets below)

### Building & Running

- **Backend**: .NET 10 minimal API in `src/Dashboard/`
- **Frontend**: Vue 3 + Vite SPA in `src/Dashboard/client/`

```bash
# Build the Vue frontend to wwwroot/
cd src/Dashboard/client
npm install
npm run build

# Start the .NET backend (must set Development environment)
cd ../
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --urls "http://localhost:5000"

# Open http://localhost:5000
```

> **Important**: You must set `ASPNETCORE_ENVIRONMENT=Development` before running. Without it, the app defaults to Production, loads `appsettings.Production.json`, and causes OAuth redirect_uri mismatch errors.

### Secrets

- `appsettings.Local.json` — local dev GitHub and Microsoft OAuth secrets (gitignored, only loaded in Development)
- `appsettings.Production.json` — production OAuth secrets (gitignored)
- `appsettings.json` — base config with empty placeholders (committed)

### Project Structure

```
src/Dashboard/
├── Program.cs              # Auth, SSE chat, models, version endpoints
├── Tools/                  # AI agent tools (QueryAzure, QueryGraph, RunScript, etc.)
├── client/src/components/  # Vue 3 components (ChatView, LoginScreen, Dashboard)
├── deploy.ps1              # Azure App Service deployment
└── startup.sh              # App Service startup — installs Python/tools
```

### Code Conventions

- **Backend**: Clean C# following Microsoft coding conventions, .NET 10 APIs, Vue 3 Composition API with `<script setup>`
- **Tools**: Return raw API JSON (let the LLM interpret it), use `string` parameters, keep tools simple
- **Frontend**: Modern JavaScript, ECharts for visualization, SSE for streaming

See the [README](README.md) for full architecture details.

## Reporting Issues

Please use [GitHub Issues](https://github.com/Azure-Samples/azure-finops-agent/issues) to report bugs or request features.
