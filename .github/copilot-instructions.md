# Azure FinOps Agent - Copilot Instructions

## Project Purpose

Azure FinOps Agent is an AI-powered agentic solution for FinOps and InfraOps on Azure. It serves as a reference architecture and delivery accelerator (VBD) for Microsoft Customer Success teams to help Azure customers optimize cloud costs.

The agent acts as a frontend on top of Microsoft IQ and Microsoft Graph APIs to:

- **Simulate potential cost outcomes** — model scenarios and forecast Azure spend under different configurations.
- **Surface immediate cost-optimization actions** — identify low-hanging fruit such as idle resources, oversized VMs, and unattached disks.
- **Provide actionable FinOps insights** — deliver data-driven recommendations aligned with the FinOps Framework.

## Tech Stack

- **Backend**: .NET API
- **Frontend**: JavaScript (UI for interacting with the agent)
- **AI**: Azure OpenAI for agentic reasoning and natural language interaction
- **Data Sources**: Microsoft IQ, Microsoft Graph APIs, Azure Cost Management APIs
- **License**: MIT

## Architecture Principles

- The solution follows an **agentic architecture** where the AI agent orchestrates calls to multiple data sources and tools to answer user questions about cloud cost and infrastructure optimization.
- Data should be retrieved from Azure APIs at runtime — the agent does not store customer cost data persistently.
- The solution is designed for **customer deployment** — customers deploy it in their own Azure subscription.
- Follow Azure SDK best practices and use async patterns for API calls.

## Code Conventions

- Use clean, well-structured C# for the .NET backend following Microsoft coding conventions.
- Use modern JavaScript/TypeScript patterns for the frontend.
- Keep API endpoints RESTful and well-documented.
- Include proper error handling for Azure API calls (rate limiting, authentication failures, etc.).
- All Azure resource interactions should use managed identity where possible.

## Key Terminology

- **FinOps**: Cloud financial management discipline combining finance, technology, and business to optimize cloud spend.
- **InfraOps**: Infrastructure operations — managing and optimizing cloud infrastructure.
- **VBD**: Value-Based Delivery — a Microsoft Customer Success engagement model.
- **MCAPS**: Microsoft Customer & Partner Solutions — the Microsoft org that delivers customer success.

## Playwright for Portal Operations

When you need to perform actions on Azure portals, GitHub, or the Microsoft Open Source Management portal (e.g., elevating JIT permissions, configuring repo settings, managing team membership), use **Playwright MCP** to navigate and interact with the portal UI directly from VS Code. This is especially useful for:

- Elevating JIT admin permissions on the repo via the Open Source portal.
- Managing GitHub team membership or repo settings.
- Any portal-based operation that cannot be done via CLI or API.

Always use Playwright when portal interaction is required rather than asking the user to do it manually.

## Self-Maintenance

Whenever code changes, new files, or documentation updates are made in this repository, **always update this instructions file** to reflect the current state of the project. This includes:

- New or removed components, services, or folders.
- Changes to the tech stack or architecture.
- New conventions, patterns, or dependencies introduced.
- Updates to team members or ownership.

This file must always be kept up to date so that Copilot has accurate context about the project.

## Repository Context

- This repo lives in the `Azure-Samples` GitHub organization.
- It is an open-source sample/accelerator, not a Microsoft product.
- Direct owners: Ali Farahnak, Klaus Gjelstrup Nielsen, Anders Ravnholt, Abishek Narayan.
