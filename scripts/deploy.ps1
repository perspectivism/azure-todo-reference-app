#Requires -Version 7.0
<#
.SYNOPSIS
    Deploys the Todo PoC environment (infrastructure + Function App + Blazor app) to Azure.

.DESCRIPTION
    Modes:

    (default) Interactive first deployment or redeployment, run by a signed-in user:
      1. shows the target context, SKUs, and the planned prerequisite writes (Entra create phase + resource group)
         and asks for confirmation BEFORE any write
      2. runs setup-entra.ps1 -Phase Create and creates the resource group if missing
      3. runs and shows the Bicep what-if preview
      4. asks for a second confirmation covering infrastructure, the Entra finalize phase, and application deployment
      5. deploys infrastructure
      6. runs setup-entra.ps1 -Phase Finalize (redirect URI + managed-identity federated credential)
      7. deploys the Function App and Blazor app packages with the caller's Entra identity (no publishing credentials)
      8. runs smoke tests

    -WhatIfOnly: strict zero-write preview for an existing, bootstrapped environment. Never creates or changes Entra
      registrations or the resource group and never deploys. If the registrations or resource group do not exist yet,
      it reports that a first-run preview requires the interactive bootstrap and exits without changes.

    -Yes: non-interactive (CI) redeployment only. Requires the resource group, the finalized todo-api/todo-web
      registrations, and their client ids (TODOAPP_API_CLIENT_ID, TODOAPP_WEB_CLIENT_ID) to exist already. Skips both
      setup-entra.ps1 phases and never calls Microsoft Graph.

    Configuration (non-secret) is read from environment variables; see docs/configuration.md:
      TODOAPP_TENANT_ID, TODOAPP_API_CLIENT_ID, TODOAPP_WEB_CLIENT_ID, TODOAPP_BUDGET_EMAIL (required),
      TODOAPP_LOCATION, TODOAPP_NAME_PREFIX, TODOAPP_WEB_PLAN_SKU, TODOAPP_BUDGET_AMOUNT, TODOAPP_LOG_DAILY_CAP_GB.

    Returns non-zero on failure.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('dev', 'prod')]
    [string] $Environment,

    [switch] $WhatIfOnly,

    [switch] $Yes
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$paramFile = Join-Path $repoRoot "infra/parameters/$Environment.bicepparam"
$mainBicep = Join-Path $repoRoot 'infra/main.bicep'
$setupEntra = Join-Path $PSScriptRoot 'setup-entra.ps1'
$artifacts = Join-Path $repoRoot "artifacts/deploy-$Environment"

function Write-Section([string] $Text) { Write-Host ''; Write-Host "==> $Text" -ForegroundColor Cyan }

function Invoke-Az {
    param([string[]] $Arguments, [switch] $Json)
    $all = @($Arguments) + @('--only-show-errors')
    if ($Json) { $all += @('--output', 'json') }
    $output = & az @all 2>&1
    if ($LASTEXITCODE -ne 0) { throw "az $($Arguments[0..1] -join ' ') failed: $($output | Out-String)" }
    if ($Json) {
        $text = ($output | Out-String).Trim()
        return $(if ($text) { $text | ConvertFrom-Json -Depth 32 } else { $null })
    }
    return $output
}

function Confirm-Continue([string] $Question) {
    Write-Host ''
    Write-Host $Question -ForegroundColor Yellow
    try { $answer = Read-Host "Type 'yes' to continue" } catch { $answer = '' }
    if ($answer -ne 'yes') { throw 'Cancelled by the operator. No further changes were made.' }
}

function Test-Guid([string] $Value) { $g = [guid]::Empty; return [guid]::TryParse($Value, [ref] $g) }

function Find-EntraApp([string] $DisplayName) {
    # Read-only Microsoft Graph lookup (never used with -Yes).
    $apps = @(Invoke-Az @('ad', 'app', 'list', '--display-name', $DisplayName, '--query', '[].appId') -Json)
    if ($apps.Count -gt 1) { throw "More than one app registration is named '$DisplayName'." }
    return $apps | Select-Object -First 1
}

function New-ZipPackage([string] $Project, [string] $Name) {
    $out = Join-Path $artifacts $Name
    $zip = Join-Path $artifacts "$Name.zip"
    Remove-Item -Recurse -Force $out, $zip -ErrorAction SilentlyContinue
    dotnet publish $Project -c Release -o $out --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $Project." }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
    return $zip
}

try {
    if ($WhatIfOnly -and $Yes) { throw '-WhatIfOnly and -Yes cannot be combined.' }
    $mode = if ($WhatIfOnly) { 'what-if preview (zero-write)' } elseif ($Yes) { 'non-interactive redeployment (-Yes)' } else { 'interactive deployment' }

    Write-Section 'Prerequisites'
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'check-prerequisites.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Prerequisite check failed.' }

    Write-Section 'Azure context'
    $account = $null
    try { $account = Invoke-Az @('account', 'show') -Json } catch { $account = $null }
    if (-not $account) { throw 'Not signed in to Azure CLI. Sign in (az login, or OIDC in CI) and select the target subscription.' }

    $namePrefix = if ($env:TODOAPP_NAME_PREFIX) { $env:TODOAPP_NAME_PREFIX } else { 'todo' }
    $location = if ($env:TODOAPP_LOCATION) { $env:TODOAPP_LOCATION } else { 'eastus2' }
    $webPlanSku = if ($env:TODOAPP_WEB_PLAN_SKU) { $env:TODOAPP_WEB_PLAN_SKU } else { 'F1' }
    $budgetAmount = if ($env:TODOAPP_BUDGET_AMOUNT) { $env:TODOAPP_BUDGET_AMOUNT } else { '10' }
    $dailyCap = if ($env:TODOAPP_LOG_DAILY_CAP_GB) { $env:TODOAPP_LOG_DAILY_CAP_GB } else { '0.1' }
    $resourceGroup = "rg-$namePrefix-$Environment"
    if ($webPlanSku -notin @('F1', 'B1')) { throw "TODOAPP_WEB_PLAN_SKU must be F1 or B1 (got '$webPlanSku')." }
    if (-not $env:TODOAPP_TENANT_ID) { $env:TODOAPP_TENANT_ID = $account.tenantId }
    if ($env:TODOAPP_TENANT_ID -ne $account.tenantId) { throw "TODOAPP_TENANT_ID ($env:TODOAPP_TENANT_ID) does not match the signed-in tenant ($($account.tenantId))." }
    if (-not $env:TODOAPP_BUDGET_EMAIL) { throw 'TODOAPP_BUDGET_EMAIL (budget alert and APIM publisher email) is required.' }

    Write-Host "Mode:            $mode"
    Write-Host "Subscription:    $($account.name) ($($account.id))"
    Write-Host "Tenant:          $($account.tenantId)"
    Write-Host "Signed in as:    $($account.user.name) ($($account.user.type))"
    Write-Host "Environment:     $Environment"
    Write-Host "Region:          $location"
    Write-Host "Resource group:  $resourceGroup"
    Write-Host ''
    Write-Host 'Intended SKUs / tiers:'
    Write-Host "  Blazor host         App Service Linux $webPlanSku$(if ($webPlanSku -eq 'B1') { '  (PAID: Basic tier, chosen explicitly)' } else { '  (Free)' })"
    Write-Host '  Function App        Flex Consumption (FC1), 0 always-ready instances, max 1 instance, 2048 MB'
    Write-Host '  API Management      Consumption (usage-based)'
    Write-Host '  Cosmos DB           NoSQL, serverless (usage-based), key auth disabled'
    Write-Host '  Storage             Standard_LRS (Function host + deployment package), shared-key access disabled'
    Write-Host "  Log Analytics       PerGB2018, daily cap $dailyCap GB, 30-day retention; workspace-based Application Insights"
    Write-Host "  Budget alert        $budgetAmount per month -> $env:TODOAPP_BUDGET_EMAIL"
    Write-Host 'Expected cost for low-volume demo use is small but not guaranteed to be zero (usage-based APIM, Cosmos,'
    Write-Host 'Functions, storage, and log ingestion). Verify current pricing. Budget alerts are delayed and are not a limit.'

    Write-Section 'Validate Bicep'
    az bicep build --file $mainBicep --stdout | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Bicep build failed.' }
    az bicep build-params --file $paramFile --stdout | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Bicep parameter file build failed.' }

    Write-Section 'Region capability checks (read-only)'
    $flexLocations = @(Invoke-Az @('functionapp', 'list-flexconsumption-locations', '--query', '[].name') -Json) | ForEach-Object { $_.ToLowerInvariant().Replace(' ', '') }
    if ($flexLocations -notcontains $location.ToLowerInvariant()) {
        throw "Flex Consumption is not available in '$location'. Choose a supported region (TODOAPP_LOCATION); no other plan is substituted."
    }
    $flexRuntimes = Invoke-Az @('functionapp', 'list-flexconsumption-runtimes', '--location', $location, '--runtime', 'dotnet-isolated') -Json
    if (($flexRuntimes | ConvertTo-Json -Depth 16) -notmatch '"10\.0"') {
        throw "Flex Consumption in '$location' does not list dotnet-isolated 10.0."
    }
    $webRuntimes = @(Invoke-Az @('webapp', 'list-runtimes', '--os', 'linux') -Json)
    if (-not ($webRuntimes | Where-Object { $_ -match '^DOTNETCORE[:|]10\.0$' })) {
        throw 'App Service Linux does not list DOTNETCORE 10.0.'
    }
    Write-Host "Flex Consumption with dotnet-isolated 10.0 and App Service Linux DOTNETCORE 10.0 are available in $location."

    $rgExists = (Invoke-Az @('group', 'exists', '--name', $resourceGroup) | Out-String).Trim() -eq 'true'

    # Resolve Entra client ids.
    Write-Section 'Entra registrations'
    $apiDisplayName = "todo-api-$Environment"
    $webDisplayName = "todo-web-$Environment"
    if ($Yes) {
        # CI path: ids must be supplied; no Microsoft Graph calls.
        if (-not (Test-Guid $env:TODOAPP_API_CLIENT_ID) -or -not (Test-Guid $env:TODOAPP_WEB_CLIENT_ID)) {
            throw 'TODOAPP_API_CLIENT_ID and TODOAPP_WEB_CLIENT_ID must be set to the bootstrapped registrations. -Yes never creates or repairs Entra state; run an interactive deployment first.'
        }
        if (-not $rgExists) { throw "Resource group '$resourceGroup' does not exist. -Yes is for redeployment only; run an interactive deployment first." }
    }
    elseif ($WhatIfOnly) {
        if (-not (Test-Guid $env:TODOAPP_API_CLIENT_ID)) { $env:TODOAPP_API_CLIENT_ID = Find-EntraApp $apiDisplayName }
        if (-not (Test-Guid $env:TODOAPP_WEB_CLIENT_ID)) { $env:TODOAPP_WEB_CLIENT_ID = Find-EntraApp $webDisplayName }
        if (-not (Test-Guid $env:TODOAPP_API_CLIENT_ID) -or -not (Test-Guid $env:TODOAPP_WEB_CLIENT_ID) -or -not $rgExists) {
            Write-Host "The environment is not bootstrapped yet (registrations found: api=$([bool]$env:TODOAPP_API_CLIENT_ID), web=$([bool]$env:TODOAPP_WEB_CLIENT_ID); resource group exists: $rgExists)." -ForegroundColor Yellow
            Write-Host 'A first-run preview requires the interactive bootstrap: run scripts/deploy.ps1 without -WhatIfOnly. No changes were made.' -ForegroundColor Yellow
            exit 1
        }
    }
    else {
        Write-Host 'Planned prerequisite writes (Entra create phase, from setup-entra.ps1 -DryRun):'
        & $setupEntra -Phase Create -Environment $Environment -DryRun | Out-Null
        if (-not $rgExists) { Write-Host "[planned] create resource group '$resourceGroup' in $location" -ForegroundColor Yellow }
        Confirm-Continue 'Confirmation 1 of 2: apply the Entra create phase and create the resource group if missing?'

        $entra = & $setupEntra -Phase Create -Environment $Environment | Select-Object -Last 1
        if (-not $entra -or -not (Test-Guid $entra.ApiClientId) -or -not (Test-Guid $entra.WebClientId)) { throw 'setup-entra.ps1 -Phase Create failed.' }
        $env:TODOAPP_API_CLIENT_ID = $entra.ApiClientId
        $env:TODOAPP_WEB_CLIENT_ID = $entra.WebClientId

        if (-not $rgExists) {
            Invoke-Az @('group', 'create', '--name', $resourceGroup, '--location', $location, '--tags', 'project=azure-todo-reference-app', "environment=$Environment") | Out-Null
            Write-Host "Created resource group '$resourceGroup'."
        }
    }
    Write-Host "todo-api client id: $env:TODOAPP_API_CLIENT_ID"
    Write-Host "todo-web client id: $env:TODOAPP_WEB_CLIENT_ID"

    # Keep the budget's original start date on redeployment (a budget's start date cannot be moved).
    $budgetName = "$namePrefix-$Environment-budget"
    $budgetStart = (Get-Date -Day 1).ToString('yyyy-MM-dd')
    try {
        $existingBudget = Invoke-Az @('rest', '--method', 'GET', '--uri', "https://management.azure.com/subscriptions/$($account.id)/resourceGroups/$resourceGroup/providers/Microsoft.Consumption/budgets/$($budgetName)?api-version=2024-08-01") -Json
        if ($existingBudget.properties.timePeriod.startDate) { $budgetStart = ([datetime] $existingBudget.properties.timePeriod.startDate).ToString('yyyy-MM-dd') }
    }
    catch {
        Write-Verbose 'No existing budget; using the first day of the current month.'
    }

    $deploymentArgs = @('--resource-group', $resourceGroup, '--parameters', $paramFile, '--parameters', "budgetStartDate=$budgetStart")

    Write-Section 'What-if preview'
    Invoke-Az (@('deployment', 'group', 'what-if') + $deploymentArgs) | ForEach-Object { Write-Host $_ }
    if ($WhatIfOnly) {
        Write-Host 'What-if completed. No changes were made.' -ForegroundColor Green
        exit 0
    }

    if (-not $Yes) {
        Confirm-Continue ('Confirmation 2 of 2: deploy the infrastructure above, run the Entra finalize phase (redirect URI and ' +
            'managed-identity federated credential on todo-web), and deploy the Function App and Blazor app packages?')
    }

    Write-Section 'Deploy infrastructure'
    $deploymentName = "todo-$Environment-$(Get-Date -Format 'yyyyMMddHHmmss')"
    $outputs = Invoke-Az (@('deployment', 'group', 'create', '--name', $deploymentName) + $deploymentArgs + @('--query', 'properties.outputs')) -Json
    $functionAppName = $outputs.functionAppName.value
    $functionAppUrl = $outputs.functionAppUrl.value
    $webAppName = $outputs.webAppName.value
    $webAppUrl = $outputs.webAppUrl.value
    $apimUrl = $outputs.apimGatewayUrl.value
    Write-Host "Function App: $functionAppName ($functionAppUrl)"
    Write-Host "Web app:      $webAppName ($webAppUrl)"
    Write-Host "APIM gateway: $apimUrl"

    if (-not $Yes) {
        Write-Section 'Entra finalize phase'
        & $setupEntra -Phase Finalize -Environment $Environment -WebBaseUrl $webAppUrl -WebManagedIdentityPrincipalId $outputs.webIdentityPrincipalId.value
        if ($LASTEXITCODE -ne 0) { throw 'setup-entra.ps1 -Phase Finalize failed.' }
    }

    Write-Section 'Deploy application packages'
    New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
    $apiZip = New-ZipPackage (Join-Path $repoRoot 'src/Todo.Api/Todo.Api.csproj') 'api'
    $webZip = New-ZipPackage (Join-Path $repoRoot 'src/Todo.Web/Todo.Web.csproj') 'web'
    Invoke-Az @('functionapp', 'deployment', 'source', 'config-zip', '--resource-group', $resourceGroup, '--name', $functionAppName, '--src', $apiZip) | Out-Null
    Write-Host 'Function App package deployed.'
    Invoke-Az @('webapp', 'deploy', '--resource-group', $resourceGroup, '--name', $webAppName, '--src-path', $webZip, '--type', 'zip') | Out-Null
    Write-Host 'Blazor app package deployed.'

    Write-Section 'Smoke tests'
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'smoke-test.ps1') -BaseUrl $apimUrl
    if ($LASTEXITCODE -ne 0) { throw 'Smoke test through APIM failed.' }
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'smoke-test.ps1') -BaseUrl $functionAppUrl
    if ($LASTEXITCODE -ne 0) { throw 'Smoke test against the Function hostname failed.' }

    Write-Host ''
    Write-Host "Deployment of '$Environment' completed." -ForegroundColor Green
    Write-Host "Web app: $webAppUrl"
    Write-Host "API (APIM): $apimUrl"
    exit 0
}
catch {
    Write-Host "deploy: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
