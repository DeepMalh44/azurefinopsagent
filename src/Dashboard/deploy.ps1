<#
.SYNOPSIS
    Deploys Azure FinOps Agent Dashboard to Azure App Service.

.DESCRIPTION
    Builds the Vue frontend, publishes the .NET backend, and deploys to Azure App Service.
    Sets GitHub OAuth secrets as App Service configuration (encrypted at rest).

.PARAMETER ResourceGroup
    Azure resource group name.

.PARAMETER AppName
    Azure App Service name.

.PARAMETER Location
    Azure region (default: westeurope).

.PARAMETER SkipInfra
    Skip creating the resource group and App Service (use if they already exist).

.EXAMPLE
    .\deploy.ps1 -ResourceGroup "rg-finops-agent" -AppName "azure-finops-agent"
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroup,

    [Parameter(Mandatory = $true)]
    [string]$AppName,

    [string]$Location = "westeurope",

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

    Write-Host "  App Service plan: ${AppName}-plan (Linux, B1)"
    az appservice plan create `
        --name "${AppName}-plan" `
        --resource-group $ResourceGroup `
        --sku B1 `
        --is-linux `
        --output none

    Write-Host "  Web app: $AppName (.NET 10)"
    az webapp create `
        --name $AppName `
        --resource-group $ResourceGroup `
        --plan "${AppName}-plan" `
        --runtime "DOTNETCORE:10.0" `
        --output none
}
else {
    Write-Host "`n[2/6] Skipping infrastructure (--SkipInfra)" -ForegroundColor Gray
}

# ── 3. Configure app settings (secrets) ──
Write-Host "`n[3/6] Configuring app settings..." -ForegroundColor Yellow

# Read production secrets from local file
$prodSettings = Get-Content "$root\appsettings.Production.json" -Raw | ConvertFrom-Json
$clientId = $prodSettings.GitHub.ClientId
$clientSecret = $prodSettings.GitHub.ClientSecret

# Derive build info from git
$buildSha = (git -C $root rev-parse --short HEAD 2>$null) ?? "unknown"
$buildNumber = (git -C $root rev-list --count HEAD 2>$null) ?? "0"

az webapp config appsettings set `
    --name $AppName `
    --resource-group $ResourceGroup `
    --settings `
    "GitHub__ClientId=$clientId" `
    "GitHub__ClientSecret=$clientSecret" `
    "ASPNETCORE_ENVIRONMENT=Production" `
    "BUILD_SHA=$buildSha" `
    "BUILD_NUMBER=$buildNumber" `
    --output none

# Set custom startup command to install Python/SQL tools before starting the app
az webapp config set `
    --name $AppName `
    --resource-group $ResourceGroup `
    --startup-file "/home/site/wwwroot/startup.sh" `
    --output none

Write-Host "  GitHub OAuth secrets configured (encrypted at rest)" -ForegroundColor Gray
Write-Host "  Build: #$buildNumber ($buildSha)" -ForegroundColor Gray
Write-Host "  Startup script: startup.sh (installs Python, pandas, numpy, sqlite3)" -ForegroundColor Gray

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
Write-Host "  Custom domain: https://azure-finops-agent.com (configure in portal)" -ForegroundColor Cyan
