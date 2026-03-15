# Azure FinOps Agent

An AI-powered agentic solution for **FinOps and InfraOps on Azure**, built as a reference architecture and delivery accelerator for Microsoft Customer Success teams.

## Overview

Azure FinOps Agent helps Azure customers optimize cloud costs by providing an intelligent agent that sits on top of Microsoft IQ and Microsoft Graph APIs. It enables users to:

- **Simulate potential cost outcomes** — model scenarios and forecast Azure spend under different configurations.
- **Surface immediate cost-optimization actions** — identify low-hanging fruit such as idle resources, oversized VMs, and unattached disks.
- **Provide actionable FinOps insights** — deliver data-driven recommendations aligned with the FinOps Framework.

## Architecture

The solution follows an agentic architecture where an AI agent (powered by Azure OpenAI) orchestrates calls to multiple Azure data sources and tools to answer user questions about cloud cost and infrastructure optimization.

| Component    | Technology                                                |
| ------------ | --------------------------------------------------------- |
| Backend API  | .NET                                                      |
| Frontend     | JavaScript                                                |
| AI Engine    | Azure OpenAI                                              |
| Data Sources | Microsoft IQ, Microsoft Graph APIs, Azure Cost Management |

## Getting Started

> **Note:** This project is under active development. Setup instructions will be added as the solution takes shape.

## Contributing

This project welcomes contributions and suggestions. Please see [CONTRIBUTING.md](CONTRIBUTING.md) for details.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Maintainers

- [Ali Farahnak](https://github.com/alfarahn) — Senior AI Architect
- [Klaus Gjelstrup Nielsen](https://github.com/klausnielsen) — Sr. Cloud Solution Architect
- [Anders Ravnholt](https://github.com/aravnholt) — Sr. Cloud Solution Architect
- [Abishek Narayan](https://github.com/abnaraya) — Cloud Solution Architect
