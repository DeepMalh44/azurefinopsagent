---
description: "Audit and update API tool descriptions against latest Microsoft docs"
---

Review all API tool descriptions in this project against the latest Microsoft documentation. Check for deprecated endpoints, new API versions, and missing operations. Query App Insights for recent tool call errors. Update tool descriptions and copilot instructions accordingly, then build to verify.

### Rules

- **Never remove a working API** unless Microsoft has fully retired it — add deprecation notes instead.
- **Keep tool descriptions concise** — the LLM has limited context; use terse format like `GET /{scope}/.../endpoint — purpose`.
- **Prefer current API versions** — use `2025-03-01` for Cost Details, `2024-08-01` for Consumption, `v1.0` for Graph (not beta, unless the endpoint is beta-only).
- **Do not add APIs that require new auth scopes** we haven't configured — our three tokens cover ARM (`management.azure.com`), Graph (`graph.microsoft.com`), and Log Analytics (`api.loganalytics.io`).
- **Always build after changes** to catch string escaping issues in C# verbatim strings.
