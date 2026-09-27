# Configuration

.NET configuration keys use `:` in JSON and `__` in environment variables and App Service/Function app settings (for example `Auth:Mode` = `Auth__Mode`). Tenant ids and client ids are identifiers, not secrets, but they are supplied through environment variables, script parameters, or GitHub variables rather than committed.

## API (`src/Todo.Api`)

| Key | Purpose | Location | Required | Secret | Environment behaviour | Example |
| --- | --- | --- | --- | --- | --- | --- |
| `Auth:Mode` | `Entra` (default) or `Dev` | `appsettings.Development.json` sets `Dev`; app setting `Auth__Mode=Entra` in Azure | no | no | `Dev` is allowed only when the host environment is Development; startup throws otherwise | `Entra` |
| `Auth:TenantId` | Tenant whose tokens are accepted (issuer and `tid`) | Function app setting (Bicep) | yes in Entra mode | no | Startup fails if missing or not a GUID in Entra mode | `72f988bf-...` |
| `Auth:ClientId` | `todo-api` application id (token audience; `api://<id>` also accepted) | Function app setting (Bicep) | yes in Entra mode | no | as above | `6a1c0b2e-...` |
| `Auth:Instance` | Authority host | default | no | no | | `https://login.microsoftonline.com/` |
| `Auth:RequiredScope` | Delegated scope required in `scp` | default | no | no | | `access_as_user` |
| `Storage:Provider` | `Cosmos` (default) or `InMemory` | default | no | no | `InMemory` is allowed only in Development (unit tests and early validation); not a supported runtime mode | `Cosmos` |
| `Cosmos:ConnectionString` | Cosmos DB Emulator connection string | `src/Todo.Api/local.settings.json` as `Cosmos__ConnectionString` (ignored by Git), or an environment variable | yes locally | **yes (emulator key)** | Local only. Copy it from the Authentication section of https://learn.microsoft.com/en-us/azure/cosmos-db/emulator. Never commit it. Startup fails with that link if neither this nor `Cosmos:Endpoint` is set. | (from the Microsoft page) |
| `Cosmos:Endpoint` | Account endpoint for managed-identity access | Function app setting (Bicep) | yes in Azure | no | Used when no connection string is set | `https://<account>.documents.azure.com:443/` |
| `Cosmos:DatabaseName` / `Cosmos:ContainerName` | Database / container | default | no | no | | `todo` / `todos` |
| `Cosmos:AutoCreate` | Create database and container on first use | `appsettings.Development.json` (`true`) | no | no | Rejected outside Development; Bicep owns them in Azure | `true` |
| `Diagnostics:EnableFaultInjection` | Enables `POST /diagnostics/fault` | Function app setting `Diagnostics__EnableFaultInjection` | no | no | `false` everywhere; enable only temporarily for exception-telemetry checks | `false` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Telemetry export | Function app setting (Bicep) | no | no (ingestion only) | Telemetry export is off when unset (local) | |
| `AzureWebJobsStorage__accountName`, `AzureWebJobsStorage__credential` | Identity-based host storage | Function app settings (Bicep) | Azure only | no | Not needed locally (HTTP-only host) | `managedidentity` |
| `FUNCTIONS_WORKER_RUNTIME`, `AZURE_FUNCTIONS_ENVIRONMENT` | Functions host | `local.settings.json` | local | no | `AZURE_FUNCTIONS_ENVIRONMENT=Development` locally; Production in Azure | `dotnet-isolated`, `Development` |

`src/Todo.Api/local.settings.json.example` lists the local settings with an empty `Cosmos__ConnectionString`. `appsettings.Development.json` is not published.

## Web (`src/Todo.Web`)

| Key | Purpose | Location | Required | Secret | Environment behaviour | Example |
| --- | --- | --- | --- | --- | --- | --- |
| `Auth:Mode` | `Entra` (default) or `Dev` | `appsettings.json` (`Entra`), `appsettings.Development.json` (`Dev`) | no | no | `Dev` only in Development; startup throws otherwise | `Entra` |
| `AzureAd:TenantId` | Tenant | App setting `AzureAd__TenantId` (Bicep) | Entra mode | no | Startup fails if missing | |
| `AzureAd:ClientId` | `todo-web` application id | App setting `AzureAd__ClientId` (Bicep) | Entra mode | no | | |
| `AzureAd:Instance`, `CallbackPath`, `SignedOutCallbackPath` | OIDC settings | `appsettings.json` | no | no | | `/signin-oidc` |
| `AzureAd:ClientCredentials:0:SourceType` | Client credential type | `appsettings.json` | Entra mode | no | `SignedAssertionFromManagedIdentity` (no secret) | |
| `AzureAd:ClientCredentials:0:ManagedIdentityClientId` | Client id of the web app's user-assigned identity | App setting (Bicep) | Azure | no | | |
| `TodoApi:BaseUrl` | API base URL | `appsettings.Development.json` (`http://localhost:7071/`); App setting (APIM gateway URL) | yes | no | Startup fails if not an absolute URL | |
| `TodoApi:Scope` | Delegated scope requested for API calls | App setting (Bicep) | Entra mode | no | | `api://<todo-api id>/access_as_user` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Telemetry | App setting (Bicep) | no | no | Off when unset | |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | Honour `X-Forwarded-Proto` behind App Service TLS termination | App setting (Bicep) | Azure | no | Needed for https OIDC redirect URIs | `true` |

### Optional: real Entra sign-in locally

Not required by any automated gate. Register `https://localhost:7070/signin-oidc` on `todo-web-<env>` (in the portal, or by running `setup-entra.ps1 -Phase Finalize` yourself with the deployed `-WebBaseUrl` and `-WebManagedIdentityPrincipalId` plus `-AdditionalRedirectBaseUrls https://localhost:7070`), create a client secret for local use only, and keep it in user secrets:

```powershell
cd src/Todo.Web
dotnet user-secrets set "Auth:Mode" "Entra"
dotnet user-secrets set "AzureAd:TenantId" "<tenant-id>"
dotnet user-secrets set "AzureAd:ClientId" "<todo-web client id>"
dotnet user-secrets set "AzureAd:ClientCredentials:0:SourceType" "ClientSecret"
dotnet user-secrets set "AzureAd:ClientCredentials:0:ClientSecret" "<local-only secret>"
dotnet user-secrets set "TodoApi:Scope" "api://<todo-api client id>/access_as_user"
dotnet run --launch-profile https
```

The API must then run with `Auth__Mode=Entra`, `Auth__TenantId`, and `Auth__ClientId` (for example as environment variables before `func start`). Delete the local secret when you no longer need it.

## Deployment (`scripts/deploy.ps1`, `infra/parameters/*.bicepparam`)

The parameter files read these environment variables with `readEnvironmentVariable()`; see `docs/deployment.md` for defaults.

| Variable | Bicep parameter | Required | Secret |
| --- | --- | --- | --- |
| `TODOAPP_TENANT_ID` | `tenantId` | yes (defaults to the signed-in tenant interactively) | no |
| `TODOAPP_API_CLIENT_ID` | `apiClientId` | yes (set by the interactive bootstrap; GitHub variable for CI) | no |
| `TODOAPP_WEB_CLIENT_ID` | `webClientId` | yes (as above) | no |
| `TODOAPP_BUDGET_EMAIL` | `budgetContactEmail` | yes | no |
| `TODOAPP_LOCATION` | `location` | no (`eastus2`) | no |
| `TODOAPP_NAME_PREFIX` | `namePrefix` | no (`todo`) | no |
| `TODOAPP_WEB_PLAN_SKU` | `webPlanSku` | no (`F1`; `B1` is paid) | no |
| `TODOAPP_BUDGET_AMOUNT` | `budgetAmount` | no (`10`) | no |
| `TODOAPP_LOG_DAILY_CAP_GB` | `logAnalyticsDailyCapGb` | no (`0.1`, minimum 0.023) | no |

Other Bicep parameters with defaults in `infra/main.bicep`: `functionMaximumInstanceCount` (1, the lowest Flex Consumption allows), `functionInstanceMemoryMB` (2048), `budgetStartDate` (first of the current month; `deploy.ps1` keeps an existing budget's start date).

## GitHub (deploy workflow)

Environment or repository **variables** (no secrets are used): `AZURE_CLIENT_ID` (`todo-deployer`), `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `TODOAPP_API_CLIENT_ID`, `TODOAPP_WEB_CLIENT_ID`, `TODOAPP_BUDGET_EMAIL`, and optionally `TODOAPP_LOCATION`, `TODOAPP_NAME_PREFIX`, `TODOAPP_WEB_PLAN_SKU`, `TODOAPP_BUDGET_AMOUNT`, `TODOAPP_LOG_DAILY_CAP_GB`.

## Tests

| Variable | Purpose | Secret |
| --- | --- | --- |
| `TODOAPP_API_BASE_URL` | API base URL for `tests/Todo.IntegrationTests`; tests are skipped when unset | no |
| `TODOAPP_TARGET` | `Local` (default) or `Azure` | no |
| `TODOAPP_LOCAL_AUTH_MODE` | `Dev` (default) or `Entra`: the Auth:Mode of the local host under test | no |
| `TODOAPP_TOKEN_A`, `TODOAPP_TOKEN_B` | Access tokens for two users (Azure only) | **yes: short-lived tokens, never commit or print** |

`scripts/run-integration-tests.ps1` sets these for local runs.

## Ignored local files

`local.settings.json`, `.env*` (except `.env.example`), `appsettings.Local.json`, `secrets/`, token files, `TestResults/`, `artifacts/`, and generated Bicep JSON are listed in `.gitignore`. `scripts/check-repo-safety.ps1` fails if any of them, or a secret pattern or the emulator key, is tracked.
