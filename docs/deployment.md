# Deployment

This guide deploys one PoC environment (`dev` or `prod`) to Azure, sets up GitHub Actions deployment with OpenID Connect, and tears the environment down. Every Azure or Entra write in this guide is performed by you (or by the GitHub workflow you start); nothing is deployed automatically.

## 1. Local prerequisites

Run `pwsh ./scripts/check-prerequisites.ps1` (see the README). Deployment itself needs only the .NET 10 SDK, Git, PowerShell 7, Azure CLI, and Bicep (`az bicep install`); `deploy.ps1` checks these with `check-prerequisites.ps1 -ForDeployment`.

## 2. Azure prerequisites

- An Azure subscription where you can create a resource group and role assignments (Owner, or Contributor plus Role Based Access Control Administrator, at subscription scope).
- Resource providers registered in the subscription: `Microsoft.Web`, `Microsoft.ApiManagement`, `Microsoft.DocumentDB`, `Microsoft.Storage`, `Microsoft.OperationalInsights`, `Microsoft.Insights`, `Microsoft.ManagedIdentity`, `Microsoft.Consumption`. Check with `az provider show -n <namespace> --query registrationState`; register with `az provider register -n <namespace>` (a write).
- A region that offers Flex Consumption with `dotnet-isolated` 10.0 and Linux App Service with .NET 10. `deploy.ps1` checks this (read-only) and fails instead of choosing another plan.

## 3. Entra prerequisites

The interactive deployment runs `scripts/setup-entra.ps1` with your identity. You need permission to create app registrations (the tenant default "Users can register applications", or the Application Developer role) and to manage the registrations you own.

Registrations per environment:

| Registration | Purpose | Created by |
| --- | --- | --- |
| `todo-api-<env>` | The API. Single tenant, identifier URI `api://<appId>`, v2 access tokens, delegated scope `access_as_user`. Pre-authorizes `todo-web-<env>` and Azure CLI (`04b07795-8ddb-461a-bbee-02f9e1bf7b46`, used to obtain test tokens). | `setup-entra.ps1 -Phase Create` |
| `todo-web-<env>` | The Blazor app. Single tenant, delegated permission to `access_as_user`. Client credential is a federated credential backed by the web app's user-assigned managed identity; no client secret. | Create phase; redirect URI and federated credential added by `-Phase Finalize` |
| `todo-deployer` | GitHub Actions deployment identity (OIDC). Azure RBAC only; no Microsoft Graph permissions. | You, once (section 6) |

Preview the Entra changes without writing anything:

```powershell
az login --tenant <tenant-id>
pwsh ./scripts/setup-entra.ps1 -Phase Create -Environment dev -DryRun
```

### Manual fallback (Entra admin center)

If your account cannot create app registrations, ask an administrator to create them with these values, then set `TODOAPP_API_CLIENT_ID` and `TODOAPP_WEB_CLIENT_ID` before deploying. `setup-entra.ps1` finds existing registrations by display name and only fills in what is missing.

`todo-api-<env>`:

1. App registrations, **New registration**. Name `todo-api-<env>`, supported account types **Accounts in this organizational directory only**, no redirect URI.
2. **Expose an API**: set Application ID URI to `api://<application (client) id>`. Add a scope: name `access_as_user`, who can consent **Admins and users**, state **Enabled**.
3. **Manifest**: set `"requestedAccessTokenVersion": 2` (under `api`).
4. **Expose an API**, **Add a client application**: add the `todo-web-<env>` client id and `04b07795-8ddb-461a-bbee-02f9e1bf7b46` (Azure CLI), each authorized for `access_as_user`.

`todo-web-<env>`:

1. **New registration**. Name `todo-web-<env>`, single tenant. Platform **Web**, redirect URI `https://<web app host>/signin-oidc` (known after the first infrastructure deployment), front-channel logout URL `https://<web app host>/signout-callback-oidc`. Leave ID token and access token issuance (implicit grant) unchecked.
2. **API permissions**: add delegated permission `todo-api-<env>` / `access_as_user`.
3. **Certificates & secrets**, **Federated credentials**, **Add credential**, scenario **Other issuer** (managed identity):
   - Issuer: `https://login.microsoftonline.com/<tenant-id>/v2.0`
   - Subject identifier: the principal (object) id of the user-assigned identity `<prefix>-<env>-web-id` (deployment output `webIdentityPrincipalId`)
   - Audience: `api://AzureADTokenExchange`
   - Name: `appservice-uami-<env>`

Do not create a client secret.

## 4. Configuration

`deploy.ps1` and the `.bicepparam` files read these environment variables. None are secrets. See `docs/configuration.md` for all settings.

| Variable | Required | Default | Meaning |
| --- | --- | --- | --- |
| `TODOAPP_BUDGET_EMAIL` | yes | | Budget alert recipient and APIM publisher email |
| `TODOAPP_TENANT_ID` | no (interactive) / yes (CI) | signed-in tenant | Entra tenant id |
| `TODOAPP_API_CLIENT_ID`, `TODOAPP_WEB_CLIENT_ID` | CI only | from `setup-entra.ps1` | Bootstrapped registration client ids |
| `TODOAPP_LOCATION` | no | `eastus2` | Azure region |
| `TODOAPP_NAME_PREFIX` | no | `todo` | Resource name prefix (2-10 lowercase characters); resource group is `rg-<prefix>-<env>` |
| `TODOAPP_WEB_PLAN_SKU` | no | `F1` | `F1` (Free) or `B1` (Basic, paid) |
| `TODOAPP_BUDGET_AMOUNT` | no | `10` | Monthly budget in the billing currency |
| `TODOAPP_LOG_DAILY_CAP_GB` | no | `0.1` | Log Analytics daily ingestion cap in GB (minimum 0.023) |

## 5. First deployment (interactive)

```powershell
az login --tenant <tenant-id>
az account set --subscription <subscription-id>
$env:TODOAPP_BUDGET_EMAIL = 'you@example.com'
pwsh ./scripts/deploy.ps1 -Environment dev
```

The script:

1. Checks prerequisites, shows the subscription, tenant, region, resource group, intended SKUs, and cost notes, and checks region capabilities (read-only).
2. Shows the planned Entra create-phase writes (from `setup-entra.ps1 -DryRun`) and whether the resource group will be created. **Confirmation 1** covers only these writes.
3. Runs `setup-entra.ps1 -Phase Create` and creates the resource group if missing.
4. Runs and shows `az deployment group what-if`.
5. **Confirmation 2** covers the infrastructure deployment, the Entra finalize phase, and the application deployment.
6. Deploys `infra/main.bicep`.
7. Runs `setup-entra.ps1 -Phase Finalize`: adds `https://<web host>/signin-oidc` to `todo-web-<env>` and creates or updates the federated credential for the web app's managed identity.
8. Publishes both apps and deploys the packages with your Entra identity (`az functionapp deployment source config-zip`, `az webapp deploy`). Basic publishing credentials are disabled on both apps.
9. Runs `scripts/smoke-test.ps1` against the APIM gateway and the Function hostname (`/health` is 200, `/todos` without a token is 401).

Type `yes` at each confirmation; any other answer stops without further changes. Rerunning the script is safe: Entra setup and Bicep converge to the same state.

The first request after deployment can take a while (cold start). The data-plane role assignment on Cosmos DB can take a few minutes to propagate.

### Preview only

For an environment that already exists:

```powershell
pwsh ./scripts/deploy.ps1 -Environment dev -WhatIfOnly
```

`-WhatIfOnly` makes no Azure or Entra writes. It reads the existing registrations (or uses `TODOAPP_API_CLIENT_ID`/`TODOAPP_WEB_CLIENT_ID`) and the existing resource group to run what-if. On a first run, when these do not exist yet, it reports that the interactive bootstrap is required and exits with code 1 without making changes.

## 6. GitHub Actions deployment identity (`todo-deployer`) and OIDC

The `deploy` workflow (`.github/workflows/deploy.yml`, manual `workflow_dispatch`) signs in to Azure as `todo-deployer` through GitHub OIDC; no Azure credential is stored in GitHub. `todo-deployer` is an Entra application/service principal (not a managed identity) so it can exist before the resource group does.

### 6.1 Create the identity and federated credential (once, before the resource group exists)

```powershell
$repo = '<github-org>/<github-repo>'   # for example contoso/azure-todo-reference-app
$environment = 'dev'                    # GitHub Environment name, matches the workflow input

$deployerAppId = az ad app create --display-name todo-deployer --sign-in-audience AzureADMyOrg --query appId -o tsv
az ad sp create --id $deployerAppId

@{
  name        = "github-$environment"
  issuer      = 'https://token.actions.githubusercontent.com'
  subject     = "repo:${repo}:environment:$environment"
  audiences   = @('api://AzureADTokenExchange')
  description = "GitHub Actions deploy workflow, environment $environment"
} | ConvertTo-Json | Set-Content "$env:TEMP/fic.json"
az ad app federated-credential create --id $deployerAppId --parameters "@$env:TEMP/fic.json"
Remove-Item "$env:TEMP/fic.json"
```

Repeat the federated credential for `prod` if you use it. Do not add API permissions or Microsoft Graph application permissions to `todo-deployer`, and do not create a client secret.

### 6.2 Assign Azure RBAC (after the first interactive deployment created the resource group)

```powershell
$sub = az account show --query id -o tsv
$scope = "/subscriptions/$sub/resourceGroups/rg-todo-dev"
$deployerObjectId = az ad sp show --id $deployerAppId --query id -o tsv

az role assignment create --assignee-object-id $deployerObjectId --assignee-principal-type ServicePrincipal --role Contributor --scope $scope
az role assignment create --assignee-object-id $deployerObjectId --assignee-principal-type ServicePrincipal --role 'Role Based Access Control Administrator' --scope $scope
```

Role Based Access Control Administrator is needed because the Bicep template creates role assignments (Function identity to storage). Both roles are scoped to the one resource group.

### 6.3 GitHub-side configuration (manual)

In the repository, **Settings**, **Environments**, create the environment `dev` (optionally add required reviewers). Add these **variables** (not secrets; none of them is a credential):

| Variable | Value |
| --- | --- |
| `AZURE_CLIENT_ID` | `todo-deployer` application (client) id |
| `AZURE_TENANT_ID` | Tenant id |
| `AZURE_SUBSCRIPTION_ID` | Subscription id |
| `TODOAPP_API_CLIENT_ID` | `todo-api-dev` client id (from the interactive deployment output) |
| `TODOAPP_WEB_CLIENT_ID` | `todo-web-dev` client id |
| `TODOAPP_BUDGET_EMAIL` | Budget alert email |
| `TODOAPP_LOCATION` | Region used by the interactive deployment (if not `eastus2`) |
| `TODOAPP_NAME_PREFIX`, `TODOAPP_WEB_PLAN_SKU`, `TODOAPP_BUDGET_AMOUNT`, `TODOAPP_LOG_DAILY_CAP_GB` | Only if you changed them from the defaults |

The values must match the bootstrapped environment. Then run **Actions**, **deploy**, **Run workflow**, choosing the environment.

### 6.4 What CI does and does not do

The workflow builds and tests once, packages both apps, signs in with OIDC, and runs `deploy.ps1 -Yes -PackageDirectory`:

- requires the resource group and the finalized `todo-api-<env>`/`todo-web-<env>` registrations to exist, and their client ids to be supplied;
- never runs `setup-entra.ps1` and never calls Microsoft Graph, so it cannot create, change, or repair app registrations, redirect URIs, or federated credentials;
- skips the region capability checks (they need subscription-level reads) because the region was validated by the interactive bootstrap;
- runs Bicep validation and what-if, deploys infrastructure and packages, and runs the `/health` and anonymous-rejection smoke tests. GitHub Actions cannot obtain user tokens, so authenticated checks are run from your machine (see `docs/operations.md`).

If the web URL or managed identity changes, rerun the interactive deployment so the finalize phase updates Entra.

## 7. Teardown

```powershell
pwsh ./scripts/destroy.ps1 -Environment dev
```

The script lists the resources in `rg-<prefix>-<env>`, requires you to type the resource group name, deletes the resource group, and purges the soft-deleted API Management instance so the name can be reused. App registrations are kept unless you add `-RemoveEntraApps`. `-Yes` skips the typed confirmation for an intentional non-interactive teardown. `todo-deployer` and its role assignments are not touched (role assignments disappear with the resource group); delete `todo-deployer` yourself if it is no longer needed.

## 8. Troubleshooting

See the "Common deployment failures" table in `docs/operations.md`.

## 9. Cost notes

| Resource | Tier | Cost behaviour |
| --- | --- | --- |
| App Service plan | Linux F1 (default) | Free. Limits: 60 CPU minutes/day, 5 WebSockets, no Always On, 165 MB bandwidth. B1 is paid and only by explicit choice (`TODOAPP_WEB_PLAN_SKU=B1`). |
| Function App | Flex Consumption, 0 always-ready, max 1 instance, 2048 MB | Pay per execution (GB-seconds and executions) after a monthly free grant; nothing is billed while idle. |
| API Management | Consumption | Pay per call after the monthly free call allowance. |
| Cosmos DB | Serverless | Pay per request unit consumed and per GB stored. Free tier is not available for serverless accounts. |
| Storage | Standard LRS | Deployment package and host metadata; cents per month. |
| Log Analytics / Application Insights | Pay-as-you-go, daily cap 0.1 GB, 30-day retention | The first 5 GB per billing account per month is free; 0.1 GB/day stays within it. |
| Budget | Monthly alerts at 80% actual and 100% forecast | Alerts are delayed; they are a backstop, not a spending limit. |

Expect a small, usage-based monthly cost rather than a guaranteed $0. Verify current prices on the Azure pricing pages for your region before deploying. `deploy.ps1` never substitutes a more expensive tier when a preferred one is unavailable; it fails instead.

## 10. Package versions of note

- `Microsoft.Identity.Web` 4.15.0 (supports `SignedAssertionFromManagedIdentity` for the web app's client credential).
- Azure Functions worker `Microsoft.Azure.Functions.Worker` 2.52.0 with the ASP.NET Core integration 2.1.1, isolated worker on .NET 10.
