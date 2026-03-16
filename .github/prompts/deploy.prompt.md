---
mode: agent
description: "Deploy Azure FinOps Agent to Azure App Service via az CLI"
---

## Deploy to Azure

1. Run `az account show` to confirm the active Azure subscription. Show the subscription name and ID.

2. Run `az webapp list --query "[].{name:name, group:resourceGroup, state:state}" -o table` to find existing web apps.

3. Ask me which resource group and app name to use (or whether to create new ones).

4. Run the deploy script:

```powershell
cd src/Dashboard
.\deploy.ps1 -ResourceGroup "<resource-group>" -AppName "<app-name>"
```

Use `-SkipInfra` if the resource group and app service already exist.

5. After deployment, confirm the app is running by checking `https://<app-name>.azurewebsites.net/api/version`.
