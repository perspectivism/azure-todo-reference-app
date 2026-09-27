# Azure Todo Reference App

**TL;DR:** A small multi-user Todo app (Blazor Interactive Server → API Management → Azure Functions → Cosmos DB serverless, secured with Microsoft Entra ID) used to show a complete, reproducible .NET 10 delivery path on Azure: local development with the Cosmos DB Emulator, layered automated tests, Bicep infrastructure, secretless deployment (managed identities, federated credentials, GitHub OIDC), and telemetry in Application Insights. Run it locally in a few minutes; deploy it with one script.

## Purpose

The application is deliberately simple. The engineering around it is the point: reproducible setup, secure defaults, automated validation, observability, low operating cost, and documented tradeoffs.

## Architecture

```text
Browser ──SignalR/HTTPS──> Blazor Web App (App Service Linux F1)
                              │ HTTPS + delegated bearer token
                              v
                           API Management (Consumption)  validate-jwt
                              │ HTTPS + bearer token
                              v
                           Azure Functions (Flex Consumption, .NET 10 isolated)  validates the token again
                              │ managed identity + data-plane RBAC
                              v
                           Cosmos DB for NoSQL (serverless), partition key /userId
Cross-cutting: Microsoft Entra ID · Application Insights + Log Analytics · Bicep · GitHub Actions + OIDC
```

Each todo belongs to the signed-in user's Entra object id (`oid`); another user's todo is indistinguishable from a missing one. Details: [docs/architecture.md](docs/architecture.md).

## Prerequisites

- Windows 11 (the code is cross-platform; scripts need PowerShell 7)
- [Visual Studio Code](https://code.visualstudio.com/)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Git](https://git-scm.com/)
- [PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli-windows), with Bicep: `az bicep install` then `az bicep version`
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
- [Azure Cosmos DB Emulator](https://learn.microsoft.com/en-us/azure/cosmos-db/emulator)

No Docker, WSL, Node.js, or Azurite is needed. Check your machine (read-only; it never installs anything):

```powershell
pwsh ./scripts/check-prerequisites.ps1                    # add -RequireEmulator before running integration tests
```

### Recommended VS Code extensions

`.vscode/extensions.json` recommends C# Dev Kit, Azure Functions, Bicep, PowerShell, Azure Cosmos DB, and Azure Tools. They are conveniences; nothing automated depends on them.

## First-run setup (local, Cosmos DB Emulator)

1. Start the **Azure Cosmos DB Emulator** and wait until it is running (`https://localhost:8081`).
2. Create the untracked local settings file for the API:

   ```powershell
   Copy-Item src/Todo.Api/local.settings.json.example src/Todo.Api/local.settings.json
   ```

3. Open the [Cosmos DB Emulator documentation](https://learn.microsoft.com/en-us/azure/cosmos-db/emulator), copy the emulator **connection string** from its Authentication section, and paste it as the value of `Cosmos__ConnectionString` in `src/Todo.Api/local.settings.json`. This file is ignored by Git; never commit the key. (An environment variable named `Cosmos__ConnectionString` also works.)
4. Install the Git hooks (sets `core.hooksPath` for this repository only):

   ```powershell
   pwsh ./scripts/install-git-hooks.ps1
   ```

5. Build: `dotnet build`

Local development runs with `Auth:Mode=Dev` (set in `appsettings.Development.json` of both apps): the web app signs in a fixed development user, and the database and container are created automatically in the emulator.

## Run locally

```powershell
# Terminal 1: API (Azure Functions host) on http://localhost:7071
cd src/Todo.Api
func start

# Terminal 2: Blazor app on http://localhost:5230
dotnet run --project src/Todo.Web --launch-profile http
```

Open http://localhost:5230. The API alone can be exercised with the Postman collection in `postman/` (set `baseUrl` to `http://localhost:7071`).

There are no `.env` files: local settings come from `src/Todo.Api/local.settings.json` (untracked), the `appsettings.Development.json` files, and the launch profiles. See [Local configuration files](docs/configuration.md#local-configuration-files). Optional real Entra sign-in locally is described in the same document.

### Debug in VS Code

Complete the first-run setup above, start the Cosmos DB Emulator, and install the recommended extensions. Debugging needs the C# Dev Kit extension, the Azure Functions extension, and Azure Functions Core Tools (`func`, a prerequisite above). Then open the **Run and Debug** view and pick a configuration from `.vscode/launch.json`:

| Configuration | What it does |
| --- | --- |
| **Debug local stack (API + Web)** | Builds the solution, starts the Functions host (port 7071) and attaches to the API worker, and launches the Blazor app (port 5230) under the debugger. The browser opens when the app is listening. Stopping one stops both debug sessions. |
| **Debug Functions (Todo.Api)** | Builds the API, starts the Functions host, and attaches to the API worker. |
| **Debug Blazor (Todo.Web)** | Builds and launches the Blazor app. Start the API separately (task `run: api`) so the UI has data. |

How the API attach works: the Functions configuration uses the Azure Functions extension's `azureFunctions.pickProcess`, which runs the `func: host start` task (`func host start --dotnet-isolated-debug --enable-json-output`). The host starts the worker, the worker prints `Azure Functions .NET Worker (PID: …) initialized in debug mode. Waiting for debugger to attach...`, and the extension reads that process id from the host's JSON output and attaches automatically; there is no process picker. Because the worker waits for the debugger, breakpoints in startup code (`Program.cs`) and in request handling (for example `HealthFunction`, via http://localhost:7071/health) both hit.

Use the `run: api` task (plain `func start`, no debugger) when you only want the API running, for example alongside **Debug Blazor**. Do not use `func: host start` for that: its worker waits for a debugger, and the host gives up after about a minute.

Useful tasks (**Terminal**, **Run Task**): `build`, `test: unit`, `test: integration (local)`, `run: api`, `run: web`, `check prerequisites`, `check repo safety`, `bicep: build and lint`.

The Functions host logs `azure.functions.webjobs.storage ... Unhealthy: Unable to create client for AzureWebJobsStorage` every 30 seconds. This is expected locally: the HTTP-only host needs no storage account. `.vscode/settings.json` sets `azureFunctions.validateEmulators` to `false` so the Azure Functions extension does not prompt for storage or Azurite before debugging.

If debugging does not start:

- Open http://localhost:7071/health, not http://localhost:7071/: the root is the Functions host's own page and never reaches the API code.
- Stop any `run: api`, `func start`, or `dotnet run` already using ports 7071 or 5230 before debugging.
- `Failed to detect running Functions host within "60" seconds`: the attach did not complete in time (for example a slow first build). Start again, or raise `azureFunctions.pickProcessTimeout` in `.vscode/settings.json`.
- `Error exists after running preLaunchTask "…"`: the extension stops waiting when any task in the workspace exits with an error while it starts the host, such as a failed build. Fix the task in its terminal and start again.
- If the API fails at startup with a message about Cosmos DB, `local.settings.json` is missing or has no `Cosmos__ConnectionString` (see step 3 of the first-run setup), or the emulator is not running.
- After editing `.vscode/*.json`, run **Developer: Reload Window**.
- If pressing F5 does nothing at all (no debug toolbar, nothing in the Debug Console), even for **Debug Blazor**, the C# extension installation is at fault. Run **Developer: Restart Extension Host**; if that does not help, uninstall C# Dev Kit and C#, reload the window, and reinstall C# Dev Kit.

## Run tests

```powershell
dotnet test                                                  # unit + bUnit tests; integration tests are skipped without an API URL
pwsh ./scripts/run-integration-tests.ps1 -Target Local       # starts the Functions host (Cosmos Emulator), runs HTTP tests in
                                                             # Dev and Entra modes, and checks that the Blazor app starts
pwsh ./scripts/run-integration-tests.ps1 -Target Azure -BaseUrl <apim-url>   # after deployment, with TODOAPP_TOKEN_A/B
pwsh ./scripts/check-repo-safety.ps1                         # secrets, placeholder markers, workflow rules, Postman coverage
```

Integration tests are tagged `Target=Local` / `Target=Azure` and can be selected with `dotnet test --filter "Target=Local"`. See [ADR 0006](docs/adr/0006-test-strategy.md).

## What You Must Change Before Deployment

Nothing identifying is committed; you supply these values. Full details: [docs/deployment.md](docs/deployment.md) and [docs/configuration.md](docs/configuration.md).

| Item | How to set it |
| --- | --- |
| **Azure subscription** | `az login --tenant <tenant-id>` then `az account set --subscription <subscription-id>` |
| **Tenant ID** | Taken from your sign-in; for CI set `TODOAPP_TENANT_ID` / GitHub variable `AZURE_TENANT_ID` |
| **Region** | `$env:TODOAPP_LOCATION` (default `eastus2`); must offer Flex Consumption with .NET 10 (the deploy script checks) |
| **Resource naming prefix** | `$env:TODOAPP_NAME_PREFIX` (default `todo`; resource group `rg-<prefix>-<env>`) |
| **Entra app registrations** | Created by the interactive deploy (`todo-api-<env>`, `todo-web-<env>`); for CI set `TODOAPP_API_CLIENT_ID` and `TODOAPP_WEB_CLIENT_ID` to the bootstrapped client ids. Manual portal steps are in `docs/deployment.md`. |
| **GitHub organization/repository and variables** | Federated credential subject `repo:<org>/<repo>:environment:<env>` for `todo-deployer`; GitHub Environment variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `TODOAPP_API_CLIENT_ID`, `TODOAPP_WEB_CLIENT_ID`, `TODOAPP_BUDGET_EMAIL` |
| **Environment parameter values** | `infra/parameters/dev.bicepparam` / `prod.bicepparam` read the `TODOAPP_*` environment variables |
| **Budget amount and alert email** | `$env:TODOAPP_BUDGET_AMOUNT` (default 10) and `$env:TODOAPP_BUDGET_EMAIL` (required) |
| **Web plan SKU** | `$env:TODOAPP_WEB_PLAN_SKU`: `F1` (default, free; 60 CPU minutes/day, 5 WebSockets, no Always On, suitable for a demo with a few users) or `B1` (paid Basic tier; no daily CPU quota, Always On available). Choose B1 only deliberately. |
| **Custom domain** | Not supported in this PoC (F1 does not support custom domains). |

## Deploy

```powershell
$env:TODOAPP_BUDGET_EMAIL = 'you@example.com'
pwsh ./scripts/deploy.ps1 -Environment dev              # interactive; two confirmations before any write
pwsh ./scripts/deploy.ps1 -Environment dev -WhatIfOnly  # zero-write preview of an existing environment
```

The interactive run shows the subscription, region, and SKUs, then asks before it creates the Entra registrations and resource group, shows a Bicep what-if, asks again before deploying infrastructure, finalizing Entra (redirect URI + managed-identity federated credential), and deploying both apps, and finishes with smoke tests. See [docs/deployment.md](docs/deployment.md).

## Tear down

```powershell
pwsh ./scripts/destroy.ps1 -Environment dev             # type the resource group name to confirm
```

Deletes the environment's resource group and purges the soft-deleted API Management instance. App registrations are kept unless you add `-RemoveEntraApps`.

## Git hooks

`pwsh ./scripts/install-git-hooks.ps1` points `core.hooksPath` at `.githooks/`. The pre-commit hook runs `scripts/pre-commit.ps1`: repository safety checks, build, `dotnet format --verify-no-changes`, unit tests, and a Bicep build. It never deploys or runs long integration suites. Hooks are a convenience; CI and the milestone gates validate independently.

## Local milestone checkpoint commits

The project was built milestone by milestone (see `SPEC.md` section 20). After each milestone's local gates passed, a local commit `milestone N: <description>` recorded the exact gate commands, their results, open Azure/user gates, and verified Azure facts in the commit body. Fixes to a completed milestone use `milestone N fix: <description>`. History is not rewritten, and nothing is pushed automatically: pushes to GitHub are made manually. Use `git log` to read the audit trail.

## CI/CD

- **CI** (`.github/workflows/pr.yml`): on pull requests to `main` and pushes to `main`. Read-only permissions. Restore, build with warnings as errors, unit tests, formatting, vulnerable-package report, Bicep build/lint/parameter validation, repository safety checks.
- **Deploy** (`.github/workflows/deploy.yml`): manual `workflow_dispatch` with a GitHub Environment. Builds and tests once, signs in to Azure with OIDC as `todo-deployer` (no stored credential), and runs `deploy.ps1 -Yes`, which redeploys an already bootstrapped environment and never modifies Entra registrations. Deployed checks in CI are limited to `/health` and anonymous rejection.
- **Dependabot** (`.github/dependabot.yml`): NuGet and GitHub Actions.

## Cost expectations

Tiers: App Service Linux F1 (free), Functions Flex Consumption (0 always-ready, max 1 instance), APIM Consumption, Cosmos DB serverless, Standard LRS storage, Log Analytics with a 0.1 GB/day cap (within the 5 GB/month free allowance). For low-volume demo use, expect a small usage-based monthly cost, not a guaranteed $0; verify current prices for your region. A monthly budget alert is created, but alerts are delayed and do not stop spending. The deploy script never substitutes a more expensive tier; it fails instead. Tear the environment down when it is not needed. Details: [docs/deployment.md](docs/deployment.md#9-cost-notes).

## Documentation

- [Architecture](docs/architecture.md) · [Configuration](docs/configuration.md) · [Deployment](docs/deployment.md) · [Operations and KQL](docs/operations.md)
- [Security](docs/security.md) · [Threat model](docs/threat-model.md) · [SECURITY.md](SECURITY.md)
- [OpenAPI contract](docs/openapi.yaml) · [Postman collection](postman/todo-api.postman_collection.json)
- ADRs: [Blazor Interactive Server](docs/adr/0001-blazor-interactive-server.md) · [Flex Consumption](docs/adr/0002-functions-flex-consumption.md) · [Cosmos serverless](docs/adr/0003-cosmos-serverless.md) · [APIM and token validation](docs/adr/0004-apim-consumption-and-jwt-validation.md) · [Authentication design](docs/adr/0005-authentication-design.md) · [Test strategy](docs/adr/0006-test-strategy.md)
- Project contract: [SPEC.md](SPEC.md) · agent instructions: [AGENTS.md](AGENTS.md)

## License

MIT. See [LICENSE](LICENSE).
