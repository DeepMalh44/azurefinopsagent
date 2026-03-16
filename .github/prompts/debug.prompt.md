---
mode: agent
description: "Build and run the Azure FinOps Agent locally for debugging"
---

## Local Debug

1. Build the Vue frontend to `wwwroot/`:

```bash
cd src/Dashboard/client
npm install
npm run build
```

2. Start the .NET backend on port 5000:

```bash
cd src/Dashboard
dotnet run --project Dashboard.csproj --urls "http://localhost:5000"
```

3. Open http://localhost:5000 in the browser and verify the login page loads.

4. Confirm the `/api/version` endpoint responds with 200.
