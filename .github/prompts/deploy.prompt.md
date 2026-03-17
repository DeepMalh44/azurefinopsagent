---
mode: agent
description: "Deploy Azure FinOps Agent to Azure App Service via az CLI"
---

## Deploy to Azure

1. Run `az account show` to confirm the active Azure subscription. Show the subscription name and ID.

2. Run `az webapp list --query "[].{name:name, group:resourceGroup, state:state}" -o table` to find existing web apps.

3. If an existing app is found in step 2, use it automatically — do NOT ask the user for confirmation. Only ask if no app exists or multiple apps are found.

4. Run the deploy script:

```powershell
cd src/Dashboard
.\deploy.ps1 -ResourceGroup "<resource-group>" -AppName "<app-name>"
```

Use `-SkipInfra` if the resource group and app service already exist.

5. After deployment, confirm the app is running by checking `https://<app-name>.azurewebsites.net/api/version`.

6. If a custom domain is configured, also verify `https://azure-finops-agent.com/api/version`.

### Rules

- **Always deploy to the existing app** if one is found in step 2 — use `-SkipInfra` automatically. Only create new infrastructure if no app exists or if I explicitly ask.
- **Never ask for confirmation** — proceed with deployment immediately after identifying the target app.

### Notes

- The deploy script publishes with `-r linux-x64` — required for the Copilot SDK native binary on Linux App Service.
- `appsettings.Local.json` is only loaded in Development mode — it will NOT override production env vars on Azure.
- GitHub OAuth callback URL must match the custom domain: `https://azure-finops-agent.com/auth/github/callback`.
- GitHub OAuth callback URL must match the custom domain: `https://www.azure-finops-agent.com/auth/github/callback`.
