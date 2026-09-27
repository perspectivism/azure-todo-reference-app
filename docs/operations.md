# Operations

How to check, troubleshoot, and recover a deployed environment. Commands that change Azure state (app settings, deployments, teardown) are marked **write**; run them only when you intend to change the environment.

Values used below:

| Placeholder | Where to find it |
| --- | --- |
| `<rg>` | `rg-<prefix>-<env>`, for example `rg-todo-dev` |
| `<apim-url>` | `apimGatewayUrl` output of the deployment, for example `https://todo-dev-apim-xxxx.azure-api.net` |
| `<func-app>` / `<func-url>` | `functionAppName` / `functionAppUrl` outputs |
| `<web-url>` | `webAppUrl` output |
| `<api-client-id>` | `todo-api-<env>` application (client) id |

## Verify health

```powershell
# Through APIM: /health is 200 and /todos without a token is 401
pwsh ./scripts/smoke-test.ps1 -BaseUrl <apim-url>

# Directly against the Function hostname: it must also reject anonymous requests
pwsh ./scripts/smoke-test.ps1 -BaseUrl <func-url>

# With a user token: create, read, and delete a todo
pwsh ./scripts/smoke-test.ps1 -BaseUrl <apim-url> -Token $env:TODOAPP_TOKEN_A
```

`/health` is a shallow liveness check and does not touch Cosmos DB. The first request after idle time can take several seconds (Flex Consumption scales to zero; F1 has no Always On), so the smoke test retries `/health` for up to three minutes.

## Where telemetry goes

All three tiers send telemetry to one workspace-based Application Insights resource (`<prefix>-<env>-appi`), stored in the Log Analytics workspace `<prefix>-<env>-log`:

| Source | Telemetry | Cloud role name (typical) |
| --- | --- | --- |
| Blazor app (OpenTelemetry, Azure Monitor distro) | requests, outgoing API calls (dependencies), exceptions, logs | web app name |
| API Management (App Insights logger, W3C correlation) | gateway requests, errors | APIM service name |
| Function App (OpenTelemetry, Azure Monitor exporter) | requests, Cosmos dependencies, exceptions, structured logs | Function App name |

W3C `traceparent` headers flow from the Blazor app through APIM to the Function, so one operation id (trace id) links the three. Error responses from the API include a `traceId` field that is the same id.

Logs never contain access tokens, secrets, or todo titles/descriptions. Log entries carry `TodoId` and `UserId` (the caller's Entra object id) as structured properties for correlation. Header and body logging is disabled in APIM.

Open queries in the portal: Application Insights resource, then **Logs**. The queries below use the workspace table names.

## Useful KQL queries

### Failed requests (last 24 hours)

```kusto
AppRequests
| where TimeGenerated > ago(24h)
| where Success == false
| project TimeGenerated, AppRoleName, Name, ResultCode, DurationMs, OperationId
| order by TimeGenerated desc
```

### Exceptions

```kusto
AppExceptions
| where TimeGenerated > ago(24h)
| project TimeGenerated, AppRoleName, ExceptionType, OuterMessage, OperationId
| order by TimeGenerated desc
```

### Slow requests

```kusto
AppRequests
| where TimeGenerated > ago(24h)
| where DurationMs > 2000
| project TimeGenerated, AppRoleName, Name, ResultCode, DurationMs, OperationId
| order by DurationMs desc
```

### Recent API calls with latency percentiles

```kusto
AppRequests
| where TimeGenerated > ago(1h)
| summarize Calls = count(), Failures = countif(Success == false),
            P50 = percentile(DurationMs, 50), P95 = percentile(DurationMs, 95)
  by AppRoleName, Name, bin(TimeGenerated, 5m)
| order by TimeGenerated desc
```

### Correlation / trace lookup

Use the `traceId` from an error response, or an `OperationId` from any query above:

```kusto
let op = "<trace id>";
union AppRequests, AppDependencies, AppExceptions, AppTraces
| where OperationId == op
| project TimeGenerated, Type, AppRoleName, Name, ResultCode, Message, ExceptionType, DurationMs
| order by TimeGenerated asc
```

### Rejected (unauthenticated) requests at the Function

```kusto
AppTraces
| where TimeGenerated > ago(24h)
| where Message startswith "Rejected unauthenticated request"
| summarize count() by tostring(Properties.FunctionName), bin(TimeGenerated, 1h)
```

### Activity for one user

```kusto
AppTraces
| where TimeGenerated > ago(24h)
| where tostring(Properties.UserId) == "<user object id>"
| project TimeGenerated, Message, OperationId
```

### Sensitive-data spot check

Should return no rows. `eyJ` is the start of every JWT.

```kusto
union AppTraces, AppExceptions, AppRequests, AppDependencies
| where TimeGenerated > ago(7d)
| where tostring(pack_all()) has "eyJ" or tostring(pack_all()) has "Bearer "
| take 10
```

## Verify exception telemetry (fault injection)

`POST /diagnostics/fault` requires a valid token and returns 404 unless `Diagnostics__EnableFaultInjection` is `true`. The setting is `false` in every environment; enable it only for this check and disable it afterwards.

```powershell
# write: enable (restarts the Function App)
az functionapp config appsettings set -g <rg> -n <func-app> --settings Diagnostics__EnableFaultInjection=true

# trigger: expect 500 with a generic problem response containing a traceId
Invoke-WebRequest -Method Post -Uri <apim-url>/diagnostics/fault -Headers @{ Authorization = "Bearer $env:TODOAPP_TOKEN_A" } -SkipHttpErrorCheck

# write: disable again
az functionapp config appsettings set -g <rg> -n <func-app> --settings Diagnostics__EnableFaultInjection=false
```

Then, after a few minutes of ingestion delay:

```kusto
AppExceptions
| where TimeGenerated > ago(1h)
| where OuterMessage has "Deliberate fault injected"
| project TimeGenerated, AppRoleName, ExceptionType, OuterMessage, OperationId
```

Use the `OperationId` with the correlation query to see the APIM request, the Function request, and the exception together.

## Obtain test tokens

Automated Azure tests use real access tokens from environment variables; tokens are never committed or printed. `setup-entra.ps1` pre-authorizes the Azure CLI client (`04b07795-8ddb-461a-bbee-02f9e1bf7b46`) for `access_as_user`, so no consent prompt appears.

```powershell
# User A (your normal Azure CLI profile)
az login --tenant <tenant-id>
$env:TODOAPP_TOKEN_A = az account get-access-token --scope api://<api-client-id>/access_as_user --query accessToken -o tsv

# User B: a second tenant user, in a separate Azure CLI profile folder
$env:AZURE_CONFIG_DIR = "$HOME\.azure-todo-userb"
az login --tenant <tenant-id> --allow-no-subscriptions
$env:TODOAPP_TOKEN_B = az account get-access-token --scope api://<api-client-id>/access_as_user --query accessToken -o tsv
Remove-Item Env:AZURE_CONFIG_DIR

# Run the Azure integration tests through APIM
pwsh ./scripts/run-integration-tests.ps1 -Target Azure -BaseUrl <apim-url>
```

Tokens expire after about an hour. Without `TODOAPP_TOKEN_B` the cross-user test is reported as skipped and the Azure user-isolation check stays open.

## Redeployment

- Interactive: `pwsh ./scripts/deploy.ps1 -Environment dev` (safe to repeat; Entra setup and Bicep are idempotent).
- Preview only (no writes): `pwsh ./scripts/deploy.ps1 -Environment dev -WhatIfOnly`.
- Non-interactive (CI): the `deploy` GitHub workflow runs `deploy.ps1 -Yes`, which never touches Entra registrations.

## Rollback and recovery

- **Bad application release:** redeploy the previous commit (check it out, or run the `deploy` workflow from that ref). Both apps are deployed from packages built by the script; there are no deployment slots on Flex Consumption or F1.
- **Bad infrastructure change:** revert the Bicep change and redeploy; Bicep deployments are incremental and converge to the template.
- **Lost web sessions:** the token cache is in memory, so users sign in again after a web app restart. This is expected.
- **Data:** Cosmos DB serverless keeps continuous backups according to the account's backup policy (default periodic backup). There is no application-level export in this PoC.

## Environment recreation

```powershell
pwsh ./scripts/destroy.ps1 -Environment dev          # write: deletes rg-<prefix>-dev after typed confirmation
pwsh ./scripts/deploy.ps1 -Environment dev           # write: recreates it (Entra registrations are reused)
```

`destroy.ps1` purges the soft-deleted API Management instance so its name can be reused. A deleted Log Analytics workspace stays soft-deleted for 14 days; recreating one with the same name in that period recovers it.

## Common deployment failures

| Symptom | Likely cause | Action |
| --- | --- | --- |
| `Flex Consumption is not available in '<region>'` | Region does not offer Flex Consumption or .NET 10 | Choose another region (`TODOAPP_LOCATION`). The script does not substitute another plan. |
| `The environment is not bootstrapped yet` (`-WhatIfOnly`) | First run, or registrations/resource group missing | Run the interactive `deploy.ps1` once. |
| `TODOAPP_API_CLIENT_ID and TODOAPP_WEB_CLIENT_ID must be set` (`-Yes`) | CI variables missing | Set the GitHub variables from the bootstrap output (see `docs/deployment.md`). |
| `AuthorizationFailed` creating role assignments (CI) | `todo-deployer` lacks Role Based Access Control Administrator on the resource group | Assign it after the resource group exists (see `docs/deployment.md`). |
| APIM name conflict after teardown | Soft-deleted APIM instance with the same name | `az apim deletedservice purge --service-name <name> --location <region>` |
| Web sign-in fails with `AADSTS700213` / no matching federated identity record | Finalize phase not run, or UAMI principal changed | Rerun the interactive deploy (finalize phase). |
| Sign-in fails with redirect URI mismatch | Web URL changed | Rerun the interactive deploy so finalize adds the new redirect URI. |
| Function returns 500 on Cosmos calls | Data-plane role assignment still propagating | Wait a few minutes after first deployment and retry. |
| `/health` slow or timing out on first call | Cold start (scale to zero / F1) | Retry; the smoke test already waits up to three minutes. |
| Web app stops responding late in the day | F1 60 CPU-minute daily quota reached | Wait for the daily reset, or choose B1 explicitly (paid). |
