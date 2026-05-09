# Changelog

All notable changes to **Azure FinOps Agent** are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Chat moderation gate (`AI/ChatModerator.cs`, `AI/ModerationVerdict.cs`) — pre-flight AOAI evaluation of every user prompt (5-second timeout) with fail-open on transient errors. Blocks unsafe prompts before they reach the main session.
- `moderation_notice` SSE event + amber inline banner in `ChatView.vue` — surfaces transient moderation failures to the user (network, rate-limit, timeout) instead of silent dead time.
- Single-retry on AOAI 429/503/timeout/network errors in `ChatModerator` (~300ms backoff, honors `Retry-After` header capped at 2s).
- Telemetry instruments: `finops.moderation.evaluated` (counter), `finops.moderation.blocks` (counter), `finops.moderation.duration_ms` (histogram).
- Ambiguous-affirmative intent-binding rule in `CopilotSessionFactory.SystemPrompt` — "yes/go ahead/proceed" now binds to the most recent in-chat offer instead of the loudest queued sidebar action.
- Documentation: "Chat Moderation Gate" section in `.github/copilot-instructions.md` with debug query example.

### Fixed

- `DefaultAzureCredential` 5-second hang on `VisualStudioCredential.RunProcessesAsync` — excluded `VisualStudio`, `VisualStudioCode`, `Interactive`, and `AzurePowerShell` credential providers in `CopilotSessionFactory`. Keeps AzureCli and ManagedIdentity in the chain for local dev and production.

### Security

- Moderation gate now stands between every user prompt and the Copilot session — blocks attempts to bypass refusal patterns or extract system rules.
- Reaffirmed: no DELETE HTTP methods anywhere in helpers; agent still cannot delete Azure resources directly.

## [0.1.0] - 2026-05-05

Initial public release.

### Added

- Azure FinOps Agent reference architecture targeting Azure App Service (Linux container).
- GitHub Copilot SDK 0.3.0 backend with shared `CopilotClient` and per-user `CopilotSession`.
- Azure OpenAI BYOK using **system-assigned managed identity** (`DefaultAzureCredential`) — no client secret needed for AOAI.
- Microsoft Entra ID multi-tenant OAuth with **incremental consent**:
  - Base tier: Azure ARM (`user_impersonation`)
  - License Optimization: `Organization.Read.All`, `Reports.Read.All`
  - Cost Allocation: `User.Read.All`, `Group.Read.All`
  - Log Analytics: `Data.Read`
  - Cost Exports: Azure Storage `user_impersonation`
- Custom AI tools: `QueryAzure`, `BulkAzureRequest`, `QueryGraph`, `QueryLogAnalytics`, `ListCostExportBlobs`, `ReadCostExportBlob`, `RetailPricing`, `GetAzureServiceHealth`, `RenderChart`, `RenderAdvancedChart`, `GeneratePresentation`, `GenerateScript`, `ReportMaturityScore`, `SuggestFollowUp`, `PublishFAQ`, `QueryUploadedFile`, `IdleResource*`, `Anomaly*`, `Schedule*`.
- **Read + write, never delete**: `DELETE` is blocked at the HTTP helper layer; destructive cleanup goes through `GenerateScript` so the user reviews before running.
- FinOps Maturity Framework UI (Crawl / Walk / Run) with per-level scoring.
- Server-Sent Events streaming (text deltas, tool start/done, charts, scripts, slides, scores).
- File uploads (CSV/TSV/JSON/TXT/XLSX/PDF/Parquet) with Python-backed inspection.
- OpenTelemetry end-to-end: .NET app + Copilot CLI subprocess → in-container OTel collector → Azure Application Insights.
- CI/CD via GitHub Actions: OIDC-based Azure login, ACR Buildx, App Service restart.

### Security

- HSTS, CSP, X-Frame-Options, X-Content-Type-Options, Referrer-Policy, Permissions-Policy.
- PKCE on the OAuth code flow.
- Origin/Referer CSRF check on every state-changing request.
- Absolute 8h session lifetime, 1h idle timeout.
- Crypto-random session user IDs; `SameSite=Lax`, `Secure`, `HttpOnly` cookies.
- DataProtection keys persisted to `/home/dataprotection-keys` to survive container restarts.

[Unreleased]: https://github.com/Azure-Samples/azure-finops-agent/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Azure-Samples/azure-finops-agent/releases/tag/v0.1.0
