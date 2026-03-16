---
mode: agent
description: "Build and run the Azure FinOps Agent locally for debugging"
---

## Local Debug

> **CRITICAL**: You **must** set `ASPNETCORE_ENVIRONMENT=Development` before running the backend.
> Without it, ASP.NET Core defaults to Production, which loads `appsettings.Production.json`
> (production OAuth credentials) and skips `appsettings.Local.json`. This causes a
> GitHub OAuth `redirect_uri` mismatch error ("The redirect_uri is not associated with this application").

1. Build the Vue frontend to `wwwroot/`:

```bash
cd src/Dashboard/client
npm install
npm run build
```

2. Start the .NET backend on port 5000 **with the Development environment**:

```bash
cd src/Dashboard
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --project Dashboard.csproj --urls "http://localhost:5000"
```

3. Open http://localhost:5000 in the browser and verify the login page loads.

4. Confirm the `/api/version` endpoint responds with 200.
