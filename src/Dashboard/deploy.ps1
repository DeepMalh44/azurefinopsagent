<#
.SYNOPSIS
    Deploys Azure FinOps Agent Dashboard to Azure App Service.

.DESCRIPTION
    Builds the Vue frontend, publishes the .NET backend, and deploys to Azure App Service.
    Sets GitHub OAuth secrets as App Service configuration (encrypted at rest).
    Configures startup.sh to install Python and tools for the AI agent.

.PARAMETER ResourceGroup
    Azure resource group name.

.PARAMETER AppName
    Azure App Service name.

.PARAMETER Location
    Azure region (default: canadacentral).

.PARAMETER SkipInfra
    Skip creating the resource group and App Service (use if they already exist).

.EXAMPLE
    .\deploy.ps1 -ResourceGroup "rg-finops-agent" -AppName "finops-agent"
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroup,

    [Parameter(Mandatory = $true)]
    [string]$AppName,

    [string]$Location = "canadacentral",

    [switch]$SkipInfra
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "=== Azure FinOps Agent - Deploy ===" -ForegroundColor Cyan

# ── 1. Verify az CLI is logged in ──
Write-Host "`n[1/6] Checking Azure CLI login..." -ForegroundColor Yellow
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "Not logged in. Run 'az login' first." -ForegroundColor Red
    exit 1
}
Write-Host "  Subscription: $($account.name) ($($account.id))" -ForegroundColor Gray

# ── 2. Create infrastructure (if needed) ──
if (-not $SkipInfra) {
    Write-Host "`n[2/6] Creating infrastructure..." -ForegroundColor Yellow

    Write-Host "  Resource group: $ResourceGroup"
    az group create --name $ResourceGroup --location $Location --output none

    # Use existing App Service plan if one exists, otherwise create one
    $existingPlan = az appservice plan list --resource-group $ResourceGroup --query "[0].name" -o tsv 2>$null
    if ($existingPlan) {
        Write-Host "  Using existing App Service plan: $existingPlan"
    }
    else {
        $existingPlan = "${AppName}-plan"
        Write-Host "  App Service plan: $existingPlan (Linux, B1)"
        az appservice plan create `
            --name $existingPlan `
            --resource-group $ResourceGroup `
            --sku B1 `
            --is-linux `
            --output none
    }

    Write-Host "  Web app: $AppName (.NET 10)"
    az webapp create `
        --name $AppName `
        --resource-group $ResourceGroup `
        --plan $existingPlan `
        --runtime "DOTNETCORE:10.0" `
        --output none
}
else {
    Write-Host "`n[2/6] Skipping infrastructure (--SkipInfra)" -ForegroundColor Gray
}

# ── 3. Configure app settings (secrets) ──
Write-Host "`n[3/6] Configuring app settings..." -ForegroundColor Yellow

$prodSettings = Get-Content "$root\appsettings.Production.json" -Raw | ConvertFrom-Json
$clientId = $prodSettings.GitHub.ClientId
$clientSecret = $prodSettings.GitHub.ClientSecret

# Microsoft Entra ID OAuth (optional — for Azure tenant data access)
$msClientId = $prodSettings.Microsoft.ClientId
$msClientSecret = $prodSettings.Microsoft.ClientSecret
$msTenantId = if ($prodSettings.Microsoft.TenantId) { $prodSettings.Microsoft.TenantId } else { "common" }

$buildSha = (git -C $root rev-parse --short HEAD 2>$null) ?? "unknown"
$buildNumber = (git -C $root rev-list --count HEAD 2>$null) ?? "0"

# Get App Insights connection string from Azure (if the resource exists)
$appInsightsCs = az monitor app-insights component show --app $AppName --resource-group $ResourceGroup --query connectionString --output tsv 2>$null

$settings = @(
    "GitHub__ClientId=$clientId",
    "GitHub__ClientSecret=$clientSecret",
    "ASPNETCORE_ENVIRONMENT=Production",
    "BUILD_SHA=$buildSha",
    "BUILD_NUMBER=$buildNumber"
)

# Add Microsoft OAuth settings if configured
if ($msClientId) {
    $settings += "Microsoft__ClientId=$msClientId"
    $settings += "Microsoft__ClientSecret=$msClientSecret"
    $settings += "Microsoft__TenantId=$msTenantId"
}

if ($appInsightsCs) {
    $settings += "ApplicationInsights__ConnectionString=$appInsightsCs"
}

az webapp config appsettings set `
    --name $AppName `
    --resource-group $ResourceGroup `
    --settings @settings `
    --output none

# Set startup script to install Python/tools before starting the app
az webapp config set `
    --name $AppName `
    --resource-group $ResourceGroup `
    --startup-file "/home/site/wwwroot/startup.sh" `
    --output none

Write-Host "  GitHub OAuth secrets configured (encrypted at rest)" -ForegroundColor Gray
if ($msClientId) {
    Write-Host "  Microsoft Entra ID OAuth configured (encrypted at rest)" -ForegroundColor Gray
}
Write-Host "  Build: #$buildNumber ($buildSha)" -ForegroundColor Gray

# ── 4. Build Vue frontend ──
Write-Host "`n[4/6] Building Vue frontend..." -ForegroundColor Yellow
Push-Location "$root\client"
npm ci --silent
npm run build
Pop-Location
Write-Host "  Frontend built to wwwroot/" -ForegroundColor Gray

# ── 5. Publish .NET backend ──
Write-Host "`n[5/6] Publishing .NET backend..." -ForegroundColor Yellow
$publishDir = "$root\publish"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

dotnet publish "$root\Dashboard.csproj" `
    -c Release `
    -r linux-x64 `
    --self-contained false `
    -o $publishDir `
    --nologo

Write-Host "  Published to $publishDir" -ForegroundColor Gray

# ── 6. Deploy to App Service ──
Write-Host "`n[6/6] Deploying to App Service..." -ForegroundColor Yellow
$zipPath = "$root\deploy.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Push-Location $publishDir
Compress-Archive -Path ".\*" -DestinationPath $zipPath -Force
Pop-Location

az webapp deploy `
    --name $AppName `
    --resource-group $ResourceGroup `
    --src-path $zipPath `
    --type zip `
    --clean true

# Cleanup
Remove-Item $zipPath -Force
Remove-Item $publishDir -Recurse -Force

Write-Host "`n=== Deployment complete ===" -ForegroundColor Green
Write-Host "  URL: https://${AppName}.azurewebsites.net" -ForegroundColor Cyan
Write-Host "  Custom domain: https://azure-finops-agent.com" -ForegroundColor Cyan
