# SPEC.md — Azure Todo Platform

## 1. Purpose

Build a small, polished, multi-user Todo application that demonstrates a complete Azure-hosted .NET delivery path from local development through infrastructure provisioning, deployment, testing, security, and observability.

The application itself must remain intentionally simple. The engineering around the application should be reproducible, secure by default, observable, testable, and easy to understand.

This specification is the authoritative project contract. `AGENTS.md` defines how a coding agent works against it.

### Table of Contents

- [1. Purpose](#1-purpose)
- [2. Guiding Principles](#2-guiding-principles)
- [3. Primary Technology Stack](#3-primary-technology-stack)
- [4. Supported Development Environment](#4-supported-development-environment)
- [5. Repository Structure](#5-repository-structure)
- [6. Application Architecture](#6-application-architecture)
- [7. Authentication and Authorization](#7-authentication-and-authorization)
- [8. Local Development](#8-local-development)
- [9. Prerequisite Validation](#9-prerequisite-validation)
- [10. Testing Strategy](#10-testing-strategy)
- [11. Observability](#11-observability)
- [12. Infrastructure as Code](#12-infrastructure-as-code)
- [13. Cost Optimization](#13-cost-optimization)
- [14. Deployment Scripts](#14-deployment-scripts)
  - [14.1 `scripts/deploy.ps1`](#141-scriptsdeployps1)
- [15. Git Hooks](#15-git-hooks)
- [16. GitHub Actions](#16-github-actions)
- [17. Documentation](#17-documentation)
  - [17.1 README.md](#171-readmemd)
- [18. Security Files, Git Ignore, and License](#18-security-files-git-ignore-and-license)
- [19. .NET Repository Standards](#19-net-repository-standards)
- [20. Milestones and Automated Completion Gates](#20-milestones-and-automated-completion-gates)
  - [20.1 Gate types](#201-gate-types)
  - [20.2 Milestones](#202-milestones)
  - [20.3 Phase V — Final external and user validation](#203-phase-v--final-external-and-user-validation)
- [21. Definition of Done](#21-definition-of-done)
- [22. Assumptions to Verify](#22-assumptions-to-verify)

---

## 2. Guiding Principles

### 2.1 Simplicity first

Prefer the simplest design that fully satisfies this specification.

Do not introduce additional services, abstractions, frameworks, architectural layers, deployment targets, or dependencies unless they solve a concrete requirement in this specification.

Avoid:

- speculative extensibility
- premature optimization
- unnecessary indirection
- architecture for architecture's sake
- unnecessary microservices
- unnecessary design-pattern layering
- unnecessary JavaScript tooling
- unnecessary containers
- unnecessary background services

When multiple designs satisfy the requirements, choose the smaller, easier-to-maintain design.

### 2.2 Explicitly out of scope

Do not introduce the following unless this specification is intentionally changed later:

- Kubernetes
- Redis (including a distributed token/session cache)
- Service Bus
- Event Grid
- event sourcing
- CQRS as a framework/pattern requirement
- additional microservices
- mobile applications
- offline synchronization
- social login
- organization-level multi-tenancy
- complex user administration
- AI/LLM functionality
- Docker as a required local-development dependency
- WSL as a required local-development dependency
- a separately installed Node.js/npm toolchain or Node-based project dependency. Development tools may use their own embedded runtimes internally; the repository must not require the developer to install or manage Node.js/npm

### 2.3 Honest engineering

Documentation and architectural decisions must describe real tradeoffs.

Do not describe the project as "enterprise-grade," "production-grade," or similar marketing language unless a specific statement is objectively supported.

The project should instead emphasize concrete qualities such as:

- reproducibility
- secure defaults
- automated validation
- observability
- maintainability
- low operational cost
- documented tradeoffs

### 2.4 Azure facts must be verified

Azure limits, SKUs, policy support, and feature availability change. Statements in this specification that depend on them are marked **(verify)**. The implementer must check each one against current Microsoft documentation during the milestone that depends on it. Verified facts that affect the final design must be reflected in the relevant tracked documentation or ADR.

If a verified fact contradicts this specification, stop and report it. Do not silently work around it with a different or more expensive design.

### 2.5 Decisions reserved for the user

The following are never decided by the implementer:

- selecting a paid or non-default SKU
- any Azure or Entra write operation (deployment, role assignment, app registration, deletion, login)
- any remote Git/GitHub write
- adding technology listed in section 2.2

---

## 3. Primary Technology Stack

Use the following technologies unless this specification is intentionally changed.

### Application

- .NET 10
- C#
- Blazor Web App, Interactive Server render mode
- Azure Functions v4, .NET isolated worker model
- Azure Cosmos DB for NoSQL
- Azure API Management
- Microsoft Entra ID
- Application Insights
- Azure Monitor / Log Analytics

### Hosting choices

| Component | Hosting choice | Notes |
| --- | --- | --- |
| Function App | Flex Consumption (Linux) | Linux Consumption does not support .NET 10 and is being retired. Flex Consumption supports .NET 10 isolated and identity-based storage access. |
| API Management | Consumption tier | Usage-based billing. `rate-limit-by-key` is not available on this tier; do not redesign authentication around subscription keys merely to add throttling. |
| Cosmos DB | Serverless capacity mode | Free tier is not available for serverless accounts. See section 13. |
| Blazor web host | Azure App Service, Linux Free (F1) by default | Appropriate for this low-volume PoC. Linux Free currently has a small concurrent WebSocket limit and daily compute quota; Basic (B1) requires explicit user choice. See section 13. |

### Infrastructure and automation

- Bicep
- Azure CLI
- PowerShell 7
- GitHub Actions
- GitHub-to-Azure authentication using OpenID Connect / workload identity federation
- No long-lived Azure credential stored in GitHub

### Testing

- xUnit
- bUnit for Blazor component tests
- automated HTTP/API integration tests written in xUnit
- Postman collection committed to the repository
- PowerShell smoke-test script

No part of the automated test suite may require a separately installed Node.js/npm toolchain or Node-based project dependency.

---

## 4. Supported Development Environment

### 4.1 Primary environment

- Windows 11
- Visual Studio Code
- PowerShell 7 (`pwsh`)

The project must not require WSL.

Application code should remain cross-platform where practical and must not depend on Windows-specific .NET APIs unless explicitly justified.

### 4.2 Required local software

The README must clearly list these prerequisites:

- Visual Studio Code
- .NET 10 SDK
- Git
- PowerShell 7
- Azure CLI
- Azure Functions Core Tools v4
- Azure Cosmos DB Emulator

Bicep is managed through Azure CLI:

```powershell
az bicep install
az bicep version
```

A standalone Bicep installation is not required.

Azurite is not required. The Functions app uses HTTP triggers only, which run locally without a storage account. If the Functions host demands storage for HTTP-only functions, stop and report it instead of adding Azurite. (verify)

### 4.3 Recommended VS Code extensions

The repository must include `.vscode/extensions.json` recommending:

- C# Dev Kit
- Azure Functions
- Bicep
- PowerShell
- Azure Cosmos DB
- Azure Tools

Extensions are developer conveniences. Automated project correctness must not depend on a VS Code extension.

### 4.4 Cosmos DB Emulator credentials

The emulator uses a publicly documented, well-known key. That key must not be written into any tracked file, including source, tests, docs, `.example` files, and scripts.

Instead:

- documentation links to Microsoft's emulator page, where the endpoint, key, and connection string are published: https://learn.microsoft.com/en-us/azure/cosmos-db/emulator (see the Authentication section)
- the developer copies the connection string from that page into an untracked location: `local.settings.json`, `dotnet user-secrets`, or an environment variable
- `local.settings.json.example` contains the setting name with an empty value and a comment pointing to the URL above
- code and tests that need the emulator fail fast with a message pointing to the same URL when the connection string is not configured

---

## 5. Repository Structure

The final repository should follow this structure unless a small adjustment makes the implementation materially simpler:

```text
/
├── README.md
├── SPEC.md
├── AGENTS.md
├── SECURITY.md
├── LICENSE
├── .gitignore
├── .editorconfig
├── global.json
├── Directory.Build.props
│
├── .vscode/
│   ├── extensions.json
│   ├── launch.json
│   ├── tasks.json
│   └── settings.json
│
├── .githooks/
│   └── pre-commit
│
├── .github/
│   ├── dependabot.yml
│   └── workflows/
│
├── scripts/
│   ├── check-prerequisites.ps1
│   ├── check-repo-safety.ps1
│   ├── install-git-hooks.ps1
│   ├── pre-commit.ps1
│   ├── run-integration-tests.ps1
│   ├── setup-entra.ps1
│   ├── deploy.ps1
│   ├── destroy.ps1
│   └── smoke-test.ps1
│
├── docs/
│   ├── architecture.md
│   ├── configuration.md
│   ├── deployment.md
│   ├── security.md
│   ├── threat-model.md
│   ├── operations.md
│   ├── openapi.yaml
│   └── adr/
│
├── infra/
│   ├── main.bicep
│   ├── modules/
│   └── parameters/
│
├── src/
│   ├── Todo.Contracts/
│   ├── Todo.Api/
│   └── Todo.Web/
├── tests/
│   ├── Todo.UnitTests/
│   └── Todo.IntegrationTests/
└── postman/
```

Suggested project roles:

- `Todo.Contracts`: request/response DTOs and shared validation rules (length limits). Referenced by `Todo.Api` and `Todo.Web`.
- `Todo.Api`: Azure Functions host, Todo service, storage interface with Cosmos and in-memory implementations, authentication middleware.
- `Todo.Web`: Blazor Web App.
- `Todo.UnitTests`: fast, hermetic tests, including bUnit component tests for `Todo.Web`.
- `Todo.IntegrationTests`: HTTP tests against a running API (local or Azure).

Do not add folders or projects merely to match this diagram if they have no useful content.

---

## 6. Application Architecture

Use this logical architecture:

```text
Browser
  |
  | SignalR / HTTPS
  v
Blazor Web App
Interactive Server
  |
  | HTTPS + bearer token
  v
Azure API Management (Consumption)
  |
  | HTTPS + bearer token
  v
.NET 10 Azure Functions
Isolated Worker (Flex Consumption)
  |
  | Managed Identity
  v
Azure Cosmos DB for NoSQL (serverless)

Cross-cutting:
- Microsoft Entra ID
- Managed Identity / Azure RBAC where appropriate
- Application Insights
- Azure Monitor / Log Analytics
- Bicep
- GitHub Actions + OIDC
```

### 6.1 Architectural boundaries

- Blazor must not access Cosmos DB directly.
- All Todo data access must go through the Azure Functions API.
- Azure Functions are the authoritative application API.
- Business rules and authorization must be enforced behind the API boundary.
- APIM must sit in front of the Azure Functions API in Azure.
- The API must remain independently testable without the Blazor UI.
- Keep the Functions surface in one Function App unless a concrete requirement justifies separation.
- Blazor calls the API server-to-server, so CORS is not required.

### 6.2 Blazor

Use:

- .NET 10 Blazor Web App
- Interactive Server render mode
- simple, responsive UI
- lightweight styling using Bootstrap or another small dependency already included with the standard template where practical

Do not add a large frontend framework.

The UI should include:

- sign-in/sign-out state
- todo list
- create todo
- edit todo
- complete/uncomplete todo
- delete todo
- basic validation
- clear empty/loading/error states
- simple per-user profile display (name and sign-in name) based on authenticated identity claims

The API client is a typed `HttpClient` that attaches the signed-in user's access token. The UI must not display or log the token.

Token cache: use the in-memory token cache. After an app restart, users sign in again. A distributed cache is out of scope.

### 6.3 Azure Functions

Use Azure Functions v4 with the .NET isolated worker model, HTTP triggers only.

Set the Functions route prefix to empty so routes match the table below exactly.

Functions should be thin HTTP endpoints that delegate to ordinary C# services.

Do not create one project per endpoint.

#### API contract

| Method | Route | Auth | Success | Notes |
| --- | --- | --- | --- | --- |
| GET | `/health` | anonymous | 200 | Shallow liveness check. Does not touch Cosmos. Returns no sensitive data. |
| GET | `/todos` | required | 200 | Query: `limit` (default 50, max 100), `continuationToken`. Returns `{ "items": [...], "continuationToken": string or null }`. Newest first. |
| GET | `/todos/{id}` | required | 200 | |
| POST | `/todos` | required | 201 | Sets `Location` header. |
| PUT | `/todos/{id}` | required | 200 | Full replacement of `title`, `description`, `isComplete`. Completing/uncompleting a todo is a PUT. |
| DELETE | `/todos/{id}` | required | 204 | |

Rules:

- Request bodies contain `title`, `description`, and `isComplete` only. Any other supplied field, including `userId`, `id`, and timestamps, is ignored.
- Response bodies contain `id`, `title`, `description`, `isComplete`, `createdUtc`, `updatedUtc`. `userId` is never sent to or returned from the API.
- `title` is required, trimmed, 1 to 200 characters. `description` is optional, at most 2000 characters. Limits live in `Todo.Contracts` and are shared.
- Request bodies larger than 16 KB are rejected with 413.
- Timestamps are set by the server, in UTC.
- Errors use RFC 9457 `application/problem+json` (ProblemDetails). No stack traces or internal details are returned.
- Status codes: 400 invalid or malformed request, 401 missing or invalid token, 404 unknown id, malformed id, or an id owned by another user, 413 body too large, 500 unexpected error.
- A todo owned by another user returns 404, identical to a missing todo. This avoids revealing that the item exists.
- Last write wins. Optimistic concurrency (ETags) is out of scope.

`docs/openapi.yaml` describes this contract. It is hand-maintained, and APIM imports it (Bicep `loadTextContent`).

### 6.4 Todo model

Stored fields:

```text
id            GUID string
userId        Entra object id of the owner (partition key)
title
description
isComplete
createdUtc
updatedUtc
```

### 6.5 Cosmos DB

Use Azure Cosmos DB for NoSQL, serverless capacity mode.

- database `todo`, container `todos`, partition key `/userId`
- in Azure, Bicep creates the database and container. Do not set throughput on them; serverless containers do not use provisioned throughput settings.
- in Azure, the Function's managed identity accesses data through a Cosmos DB data-plane role assignment (built-in data contributor role, scoped to the database or narrower). Entra-based data access cannot create databases or containers, which is another reason Bicep owns them.
- in Azure, disable key-based authentication on the account (`disableLocalAuth`) so applications must use Microsoft Entra authentication.
- locally, the emulator supports provisioned throughput only, not serverless. Application code is identical, but the serverless behavior is only exercised in Azure. Document this.
- database and container auto-creation is allowed only when `Cosmos:AutoCreate` is true, which is set only in local development configuration
- point reads use `id` plus partition key `userId`
- every query includes the caller's `userId` as both partition key and filter

Every query and mutation must be scoped to the authenticated user. A user must never be able to retrieve, update, or delete another user's Todo item by changing a URL, ID, request body, or query.

---

## 7. Authentication and Authorization

Use Microsoft Entra ID.

Requirements:

- anonymous users cannot use protected application functionality
- access is restricted to the configured Entra tenant
- the authenticated user identity is derived from trusted Entra claims
- API authorization must never trust a `userId` supplied by the browser/request body
- API access in Azure must require authenticated requests
- APIM validates access tokens
- the Functions/API layer also validates access tokens and enforces user isolation
- the authenticated user's Entra object identifier (`oid` claim) is the ownership key

### 7.1 Design

**Application identities** are deliberately separated:

1. `todo-api`: single-tenant Entra app registration that exposes the delegated scope `access_as_user` and issues v2 access tokens. The web registration is pre-authorized for that scope.
2. `todo-web`: single-tenant Entra app registration used by the Blazor app to sign users in and acquire delegated API tokens.
3. `todo-deployer`: separate Entra application/service principal used only by GitHub Actions through OIDC/workload identity federation to deploy Azure resources. It is not used by the application at runtime.

`todo-api` and `todo-web` are not managed in Bicep. `scripts/setup-entra.ps1` creates/configures them idempotently during an **interactive user-authorized deployment**:

- create phase before infrastructure: create/read the registrations and output their client IDs
- finalize phase after infrastructure: add the deployed redirect URIs and the managed-identity federated credential required by `todo-web`

CI/non-interactive deployment must not create, modify, or finalize `todo-api` or `todo-web` and must not require Microsoft Graph app-management permissions. Those Entra prerequisites must already be finalized by an interactive user-authorized run.

`todo-deployer` is configured separately for GitHub OIDC. It receives Azure RBAC only; do not grant it Microsoft Graph application-management permissions.

If the user's account cannot create app registrations, this is a user-only step. `docs/deployment.md` must give the exact portal steps and values.

**Web app credential (secretless):** the Blazor web app authenticates to Entra using a federated credential trusted by the `todo-web` registration, backed by a user-assigned managed identity on the App Service. No client secret is created. Use a current compatible `Microsoft.Identity.Web` version that supports `SignedAssertionFromManagedIdentity`, and record the actual package version in the relevant tracked ADR or deployment documentation.

**Token validation (in both APIM and the Function):**

- issuer is the configured tenant
- audience is the `todo-api` application
- `tid` matches the configured tenant
- scope claim `scp` contains `access_as_user`
- token lifetime is valid
- `oid` claim is present

APIM applies `validate-jwt` (or the equivalent Entra token policy) to every operation except `/health`. The Function validates the same rules in ASP.NET Core authentication middleware, not through function keys, so authorization logic stays unit-testable and works locally.

APIM Consumption has no fixed outbound IP, so the Function cannot be restricted to APIM by IP. The Function's own hostname is publicly reachable, but every protected route requires a valid token there too. Document this in `docs/security.md` and `docs/threat-model.md`.

The Functions layer uses anonymous authorization level, because function keys are credentials and the JWT is the gate. APIM does not require a subscription key.

### 7.2 Local development identity

Local development uses a development-only identity mode:

- `Auth:Mode` is `Entra` by default. `Dev` is allowed only when the host environment is `Development`.
- if `Auth:Mode` is `Dev` in any other environment, startup throws
- in `Dev` mode the API accepts an `X-Dev-User-Id` header (a GUID) as the caller's identity, and falls back to a fixed development user when absent. This lets automated tests act as two different users
- in `Dev` mode the Blazor app signs in a fixed development user
- unit tests must prove the startup guard, and that a request without a valid identity is rejected when `Auth:Mode` is `Entra`
- `Dev` must never appear in infrastructure files or deployed configuration

Optional: a developer may sign in against real Entra locally using a client secret held in `dotnet user-secrets`. This is documented as optional, and no automated gate depends on it.

### 7.3 Test identities for Azure

Automated Azure tests use real bearer tokens supplied through environment variables, never committed:

- `TODOAPP_TOKEN_A`, `TODOAPP_TOKEN_B`: access tokens for two different users in the tenant
- documented way to obtain them: `az account get-access-token --scope api://<api-client-id>/access_as_user`, with the second user obtained through a separate Azure CLI profile (`AZURE_CONFIG_DIR`). This requires the Azure CLI's first-party client to be pre-authorized on `todo-api`. (verify)
- if `TODOAPP_TOKEN_B` is not set, the cross-user Azure test reports SKIPPED and the Azure user-isolation gate remains open

GitHub Actions cannot obtain user tokens. CI-run deployed checks are limited to `/health` and anonymous rejection. Authenticated Azure tests are run by the user from their machine.

### 7.4 Credentials

No credentials may be committed to source control.

Prefer secretless authentication where supported:

- GitHub Actions to Azure: OIDC/workload identity federation
- Azure Functions to Cosmos DB: Managed Identity + RBAC
- Function App to its storage account and deployment container: Managed Identity (identity-based `AzureWebJobsStorage`, shared-key access disabled on the storage account)
- Blazor web app to Entra: federated credential via managed identity

If a credential is genuinely required, it must be provisioned outside source control and stored securely using an Azure-supported mechanism such as Key Vault or protected application configuration.

Do not add Key Vault merely to store values that can be handled securely without it.

Tenant IDs, subscription IDs, and client IDs are not secrets, but they are supplied through environment variables, deploy script parameters, or GitHub variables rather than hard-coded in tracked files. `.bicepparam` files use `readEnvironmentVariable()`.

No credential may appear in:

- source code
- checked-in JSON
- checked-in `.env` files
- checked-in `local.settings.json`
- workflow YAML
- Bicep parameter files
- Postman files

---

## 8. Local Development

Local development must support the full application without requiring an Azure deployment for every code change.

### 8.1 Local services

Use:

- Blazor Web App locally
- Azure Functions Core Tools locally
- Azure Cosmos DB Emulator locally
- Cosmos-backed Todo repository
- `Auth:Mode=Dev` (section 7.2)

The Cosmos-backed path is the supported local-development environment and is authoritative for local persistence/integration validation.

The in-memory Todo repository may still exist as an internal implementation used by unit tests and early milestone validation, but it is not a separately supported end-user runtime mode and does not require separate README setup instructions.

### 8.2 Local configuration

Do not commit local secret-bearing files.

Examples that must be ignored:

- `local.settings.json`
- `.env`
- user secrets
- local token files

Commit sanitized `.example` files when useful. Cosmos emulator credentials follow section 4.4.

### 8.3 VS Code experience

The repository must include:

- `.vscode/extensions.json`
- `.vscode/launch.json`
- `.vscode/tasks.json`
- `.vscode/settings.json`

Provide launch/tasks for at least:

- build solution
- run unit tests
- start Azure Functions
- run Blazor Web App
- debug Blazor
- debug Azure Functions
- debug/run the local application stack where reasonably possible
- Bicep validation/build

Machine-specific paths must not be committed.

---

## 9. Prerequisite Validation

Create:

```text
scripts/check-prerequisites.ps1
```

It must:

- run on PowerShell 7
- perform read-only checks
- print clear PASS / FAIL / WARN output
- print the exact installation/remediation command or guidance for anything missing where practical
- never silently install software
- return exit code `0` when all **required** checks pass, and non-zero when any required check fails

**Required** (FAIL if missing):

```powershell
dotnet --version     # .NET 10 SDK must be available
git --version
pwsh --version       # major version 7 or later
az version
az bicep version
func --version       # major version 4
```

**Recommended** (WARN if missing, does not change the exit code):

```powershell
code --version
```

Also detect whether the Azure Cosmos DB Emulator is installed, and whether it is reachable on its documented local endpoint. Both are WARN by default.

The script accepts `-RequireEmulator`. With it, an unreachable emulator is a FAIL. Milestone gates that run local integration tests use `-RequireEmulator`.

If Bicep is missing, output guidance equivalent to:

```powershell
az bicep install
```

---

## 10. Testing Strategy

Testing must be layered. The xUnit suites are the authoritative automated tests. Postman is a convenience, not a gate.

### 10.1 Static validation

Automate:

- restore
- compile
- formatting/style checks (`dotnet format --verify-no-changes`)
- Bicep build and lint for `main.bicep`, and `az bicep build-params` for every `.bicepparam` file
- vulnerable-package check (`dotnet list package --vulnerable --include-transitive`), reported in CI
- repository safety checks (`scripts/check-repo-safety.ps1`): secret patterns, tracked local-settings files, unresolved TODO/FIXME markers, and forbidden patterns in workflows (long-lived Azure credentials)

### 10.2 Unit tests

Use xUnit (and bUnit for Blazor components).

Unit tests should cover meaningful logic, including:

- validation rules
- Todo service behavior
- ownership/authorization rules, including cross-user isolation with two identities
- JWT validation rules using locally generated signing keys (no network)
- the `Dev` identity startup guard
- Blazor Todo page states (loading, empty, error, list) with a fake API client
- error handling where useful

Do not write tests solely to increase test count.

### 10.3 Integration tests

`tests/Todo.IntegrationTests` exercises real HTTP endpoints. It is parameterized by environment variables:

- `TODOAPP_API_BASE_URL`
- `TODOAPP_TOKEN_A`, `TODOAPP_TOKEN_B` (Azure only)

Tests are tagged with an xUnit trait, `Target=Local` or `Target=Azure`, and selected with `dotnet test --filter`. The same test classes should run against both targets where the behavior is identical.

`scripts/run-integration-tests.ps1 -Target Local|Azure` runs them. For `Local`, it starts the Functions host, waits for `/health`, runs the tests, and stops the host, returning non-zero on failure.

Cover at least:

- health
- list todos (including paging)
- create todo
- retrieve todo
- update todo
- complete/uncomplete todo
- delete todo
- malformed request returns 400
- unknown item returns 404
- another user's item returns 404 for read, update, and delete
- unauthenticated request returns 401 (Azure and `Auth:Mode=Entra` local)

### 10.4 Postman

Commit a Postman collection under `postman/`.

Include:

- all endpoints in section 6.3
- representative successful requests
- representative error cases
- variables for base URL and tokens

Do not commit live access tokens. The collection is validated only statically: it parses as JSON and contains a request for every route in section 6.3. This check is part of `scripts/check-repo-safety.ps1`.

### 10.5 Azure verification

During Azure verification (Phase V, section 20), validate:

- APIM to Function routing
- authentication enforcement (anonymous request rejected at APIM and at the Function hostname)
- authenticated CRUD
- user isolation with two identities
- Function to Cosmos access through the intended managed-identity path
- Application Insights telemetry
- basic Azure Monitor/Log Analytics visibility

### 10.6 UI verification

Blazor UI is verified by bUnit component tests and by an automated startup check (the app starts and its root URL responds). A full browser walkthrough is a manual user gate. It remains open until the user confirms it and is reported in the final summary. Full browser-automation frameworks are out of scope because they add tooling and maintenance complexity that is unnecessary for this PoC.

---

## 11. Observability

Use Application Insights and Azure Monitor / Log Analytics.

Requirements:

- structured application logs
- request telemetry
- exception telemetry
- correlation/trace identifiers where practical (W3C trace context across Blazor, APIM, and Functions)
- useful health/smoke-test signals
- no tokens, secrets, or Todo titles/descriptions in logs. The user's `oid` may be logged as a correlation dimension.

A fault-injection endpoint supports the exception-telemetry gate:

- `POST /diagnostics/fault` throws a deliberate exception
- it requires authentication
- it returns 404 unless `Diagnostics:EnableFaultInjection` is true
- the setting defaults to false in all environments and is enabled only temporarily during Phase V verification

Document useful KQL queries for:

- failed requests
- exceptions
- slow requests
- recent API calls
- correlation/trace lookup

Place operational guidance in:

```text
docs/operations.md
```

No extra local Log Analytics tooling is required.

---

## 12. Infrastructure as Code

Use Bicep for Azure infrastructure, deployed at resource-group scope. The resource group itself is created by `deploy.ps1`.

Provision only the resources required by this specification:

- App Service plan and web app for Blazor, with a user-assigned managed identity
- Flex Consumption plan and Function App, with a managed identity
- storage account for Functions (shared-key access disabled, no public blob access, TLS 1.2 or later) and its deployment container
- API Management, Consumption tier, with the API imported from `docs/openapi.yaml` and the JWT policy
- Cosmos DB account (serverless), database, container, and data-plane role assignment
- Application Insights (workspace-based) and Log Analytics workspace with a daily ingestion cap
- Azure RBAC assignments (Function identity to its storage account and deployment container)
- a budget alert
- configuration required for Entra integration

Keep modules small and understandable.

Do not create elaborate Bicep module hierarchies for a small project.

### 12.1 Parameterization

Provide:

```text
infra/parameters/dev.bicepparam
infra/parameters/prod.bicepparam
```

Both files must compile (`az bicep build-params`). Because Cosmos runs serverless, both environments can be deployed in the same subscription, but only one Azure environment is deployed by default.

Parameters include a name prefix, region, web plan SKU (`F1` default, `B1` only by explicit user choice), Log Analytics daily cap, and budget amount. The default Blazor host is Linux App Service. Identifying values (tenant ID, client IDs, contact email) come from `readEnvironmentVariable()` or deploy script arguments.

No secrets may be stored in parameter files.

---

## 13. Cost Optimization

This is a proof-of-concept. Keep cost minimal under normal low-volume demo usage, but do not distort the architecture merely to force every service to $0. Prefer free tiers when they fit the workload; low usage-based cost is acceptable only for SKUs explicitly named in this specification when the serverless/consumption model is a better technical fit. Cosmos serverless and APIM Consumption can bill per use, so expect a small non-zero cost rather than a guaranteed $0. Verify current pricing before deployment.

### 13.1 Cost requirements

- Cosmos DB: serverless capacity mode. Free tier is not used: it is limited to one account per subscription and is unavailable for serverless accounts. This is recorded in an ADR.
- Azure Functions: Flex Consumption with zero always-ready instances and the lowest maximum instance count the plan allows (verify)
- API Management: Consumption tier
- Blazor host: Linux App Service Free (F1) by default. At the time of this specification, Linux Free supports up to 5 concurrent WebSocket connections and has a 60 CPU-minute daily quota with no Always On. Those limits are acceptable for this low-volume PoC and must be documented. Re-verify the current F1 quotas in Phase V and include a simple WebSocket/interactive-session check. If the limits make the app impractical, stop and ask the user before considering Basic (B1).
- keep Application Insights / Log Analytics telemetry modest, with a daily ingestion cap
- do not provision Premium/Dedicated SKUs unless explicitly required by a future spec change
- do not provision Redis, Kubernetes, Service Bus, NAT Gateway, private endpoints, or paid networking infrastructure
- provision a budget alert
- teardown must remove disposable resources

### 13.2 Cost safety

The deployment process must:

- clearly show the target environment
- clearly show intended Azure SKUs/tier choices
- support `what-if` or equivalent preview
- refuse to silently substitute a more expensive SKU when a preferred one is unavailable
- fail clearly and document the reason instead of silently increasing cost

Budget alerts are delayed and are a backstop, not a limit.

### 13.3 Rate limiting

APIM Consumption does not support per-key rate limiting. Subscription-key rate limiting would require distributing a subscription key to callers, which conflicts with the secretless design. Instead:

- unauthenticated calls are rejected at APIM before they reach the Function
- Function maximum instances are capped
- serverless Cosmos and the budget alert bound the cost of abuse

The residual risk is documented in `docs/threat-model.md`.

---

## 14. Deployment Scripts

### 14.1 `scripts/deploy.ps1`

Parameters: `-Environment dev|prod`, `-WhatIfOnly`, `-Yes` (`-Yes` is intended for CI/non-interactive **redeployment only**).

General requirements:

- require PowerShell 7
- validate prerequisites
- validate Azure authentication/subscription context
- display target subscription/environment/region and intended SKUs
- validate/build Bicep and parameter files
- return non-zero on failure
- avoid printing secrets
- be safe to run repeatedly

`-WhatIfOnly` is a strict zero-write mode intended for an **existing bootstrapped environment / redeployment preview**:

- do not create or modify Entra app registrations
- do not create the resource group
- do not deploy infrastructure or application artifacts
- do not run any command that changes Azure or Entra state
- use only existing Entra/resource-group state needed to calculate the resource-group-scoped what-if
- if the required Entra registrations or target resource group do not yet exist, report that a first-run preview requires interactive bootstrap and exit without making changes

A normal interactive first deployment uses two explicit confirmation boundaries:

1. display the target context plus all planned prerequisite writes; ask for confirmation **before any write**
2. after approval, run `scripts/setup-entra.ps1` (create phase) and create the target resource group if either is missing
3. run and display the Bicep what-if preview
4. ask for a second confirmation covering infrastructure/application deployment **and the post-deployment Entra finalize phase**
5. deploy infrastructure
6. run `scripts/setup-entra.ps1` (finalize phase) using the interactive user's identity
7. deploy application artifacts (Function App and Blazor app) using the caller's identity, with no storage keys or publishing credentials
8. run smoke tests

The first confirmation covers only the bootstrap writes in step 2. The second confirmation explicitly covers the writes in steps 5–7. Do not perform a write that was not described by the confirmation that authorized it.

`-Yes` skips interactive confirmations only for CI/non-interactive redeployment and has a narrower responsibility:

- the target resource group must already exist
- `todo-api` and `todo-web` must already exist and have been finalized by an interactive user-authorized deployment
- required client IDs/configuration must already be supplied
- skip both `setup-entra.ps1` create and finalize phases entirely
- do not call Microsoft Graph to create, update, or inspect Entra application configuration
- run Bicep validation/what-if, deploy Azure infrastructure/application artifacts, and run smoke tests
- fail with a clear message if required pre-existing deployment inputs are missing rather than attempting to bootstrap or repair Entra state

### 14.2 `scripts/destroy.ps1`

Must:

- clearly show what will be removed
- require the operator to type the resource group name to confirm, unless a documented CI/non-interactive switch (`-Yes`) is intentionally supplied
- remove disposable project resources (the resource group)
- leave Entra app registrations in place unless `-RemoveEntraApps` is supplied
- avoid deleting unrelated resources
- return non-zero on failure

### 14.3 `scripts/smoke-test.ps1`

Parameters: `-BaseUrl`, optional `-Token`.

Must perform a small deterministic deployed-health/API check and return non-zero on failure:

- `GET /health` returns 200
- `GET /todos` without a token returns 401
- if a token is supplied, create, read, and delete a todo

### 14.4 `scripts/run-integration-tests.ps1` and `scripts/check-repo-safety.ps1`

Described in sections 10.3 and 10.1. Both return non-zero on failure.

---

## 15. Git Hooks

Version the hook under:

```text
.githooks/pre-commit
```

Create:

```text
scripts/install-git-hooks.ps1
scripts/pre-commit.ps1
```

`install-git-hooks.ps1` should configure:

```powershell
git config core.hooksPath .githooks
```

The pre-commit hook should remain fast.

Run, where practical:

- formatting validation
- build
- fast unit tests
- fast Bicep validation
- `scripts/check-repo-safety.ps1`

Do not run:

- Azure deployment
- full cloud integration tests
- long-running local integration suites
- destructive operations

Hooks are developer guardrails, not the authoritative quality gate.

CI and milestone completion gates must independently validate correctness because hooks can be bypassed.

---

## 16. GitHub Actions

Create workflow YAML under `.github/workflows/`. Pin third-party actions to a version tag or commit SHA. Create `.github/dependabot.yml` covering NuGet and GitHub Actions.

### 16.1 Pull request validation (`pr.yml`)

Permissions: `contents: read`. Do not use `pull_request_target`.

On pull requests:

- restore
- build
- unit tests
- formatting/static checks
- Bicep build/lint and parameter file validation
- vulnerable-package report
- `scripts/check-repo-safety.ps1`
- other fast non-cloud checks

Do not deploy from untrusted pull requests.

### 16.2 Deployment (`deploy.yml`)

Trigger: `workflow_dispatch` only, using a GitHub Environment. Automatic deployment on every push to main is not used, to keep cost and blast radius small.

- build once
- run validation/tests
- authenticate to Azure through OIDC/workload identity federation (`permissions: id-token: write`)
- deploy the configured PoC environment
- deploy application artifacts
- run deployed smoke tests (`/health` and anonymous rejection only, per section 7.3)

The GitHub deployer is the dedicated `todo-deployer` Entra application/service principal with a federated identity credential for the repository and GitHub Environment. Use an Entra application/service principal here rather than a user-assigned managed identity so the deployer identity can exist before the project resource group is created.

The user creates/configures `todo-deployer` and its GitHub federated credential. After the target resource group exists, assign `todo-deployer` **Contributor** plus **Role Based Access Control Administrator** at that resource-group scope (needed because Bicep creates role assignments). Do not grant `todo-deployer` Microsoft Graph application-management permissions; CI must not modify `todo-api` or `todo-web`.

`docs/deployment.md` must give the exact identity/federation commands, the post-resource-group RBAC commands, and the GitHub-side values to set. At minimum, document the `todo-deployer` client ID, tenant ID, subscription ID, and the bootstrapped `todo-api` and `todo-web` client IDs required by the non-interactive deployment path.

### 16.3 Remote safety

Repository automation may be authored and validated locally, but the coding agent must never push it to GitHub. The user performs all remote pushes manually and verifies that the workflows run.

---

## 17. Documentation

### 17.1 README.md

README must begin with a concise TL;DR.

When a GitHub `origin` URL is available, place one GitHub Actions CI badge for the `main` validation workflow near the top. Derive the repository path from `git remote get-url origin`; never invent it or commit a placeholder badge. If no usable GitHub origin exists yet, omit the badge and report it as `PENDING-USER` until the remote is configured.

Include:

- project purpose
- architecture summary
- prerequisites
- recommended VS Code extensions
- local-development setup using the Cosmos DB Emulator
- exact first-run steps, including starting the Cosmos DB Emulator and copying its documented local connection string into local settings (section 4.4)
- how to run locally
- how to run tests
- how to deploy
- how to tear down
- how Git hooks are installed
- how local milestone checkpoint commits work
- how CI/CD works
- cost expectations/cost-safety notes
- license
- links to deeper documentation

Include a prominent section:

```text
What You Must Change Before Deployment
```

Document at least:

- Azure subscription
- tenant ID
- region
- resource naming prefix
- Entra configuration/app registrations, including the bootstrapped `todo-api` and `todo-web` client IDs
- GitHub organization/repository values needed for workload identity and the GitHub Environment/repository variables consumed by deployment
- environment parameter values
- budget amount and alert email
- web plan SKU choice (F1 or B1) and its tradeoffs
- optional custom domain values if supported

### 17.2 `docs/configuration.md`

Document every meaningful configurable value:

- purpose
- location
- required/optional
- secret/non-secret
- environment-specific behavior
- example value where safe

### 17.3 `docs/architecture.md`

Include:

- logical architecture
- component responsibilities
- major request flow
- authentication/deployment identity flows, clearly distinguishing `todo-api`, `todo-web`, the App Service managed identity/federated credential, and the separate GitHub `todo-deployer` identity
- data ownership model
- observability flow

### 17.4 `docs/deployment.md`

Include:

- local prerequisites
- Azure prerequisites
- Entra prerequisites and the manual fallback for app registrations
- `todo-deployer` identity and GitHub OIDC setup, including that its resource-group RBAC assignments happen only after the target resource group exists and that it receives no Microsoft Graph app-management permission
- deployment sequence, including interactive Entra bootstrap/finalize versus CI redeployment behavior
- teardown
- troubleshooting
- cost-related deployment notes

### 17.5 `docs/security.md`

Include:

- authentication
- authorization
- secret handling
- managed identities
- RBAC
- input validation
- logging considerations
- the APIM-plus-Function double validation and why
- dependency/security assumptions

### 17.6 `docs/threat-model.md`

Keep concise.

Use a practical threat-model structure, such as STRIDE, covering at least:

- unauthorized API access
- cross-user data access / IDOR
- token theft/replay
- secret exposure
- input abuse
- excessive API requests (including the APIM Consumption limitation in section 13.3)
- direct access to the Function hostname bypassing APIM
- telemetry leakage
- deployment credential compromise
- dev identity mode leaking into a deployed environment

Document mitigations and accepted residual risk.

### 17.7 `docs/operations.md`

Include:

- how to verify health
- how to inspect failed requests
- how to inspect exceptions
- useful KQL queries
- correlation/trace lookup
- how to obtain test tokens (section 7.3)
- redeployment
- rollback/recovery approach
- environment recreation
- common deployment failures

### 17.8 ADRs

Create concise ADRs only where alternatives were genuinely weighed. Expected set:

- Blazor Interactive Server versus WebAssembly
- Functions hosting plan (Flex Consumption versus Linux Consumption)
- Cosmos DB serverless versus free tier
- APIM tier and where JWTs are validated
- Authentication design (federated credential, dev identity mode, test tokens)
- Test strategy (xUnit as the authoritative suite, Postman as a convenience)

Do not create ADRs for trivial implementation details or for choices with no considered alternative.

---

## 18. Security Files, Git Ignore, and License

Create a root `.gitignore` suitable for:

- .NET
- Visual Studio / VS Code temporary files
- Azure Functions local settings
- local environment files
- test results
- coverage output
- local secrets
- generated build artifacts

At minimum ignore:

```text
bin/
obj/
.vs/
.env
local.settings.json
appsettings.Local.json
secrets/
TestResults/
coverage/
```

Create `SECURITY.md` describing:

- supported security assumptions
- how to report a vulnerability
- secret-handling expectations
- what not to include in issues/logs

The root `LICENSE` file is the MIT License with the notice `Copyright (c) 2026 Pero Matic`. It is provided by the repository owner and must not be modified by the implementer.

---

## 19. .NET Repository Standards

Create `global.json` targeting the .NET 10 SDK (with `rollForward` of `latestFeature` and no prerelease SDKs).

Create `.editorconfig`.

Create `Directory.Build.props` for shared settings where useful.

Favor:

- nullable reference types
- deterministic builds
- warnings that catch useful problems
- analyzers that do not create excessive noise
- warnings treated as errors in CI only (`-p:TreatWarningsAsErrors=true`), not for local builds

Do not enable warning policies so strict that ordinary framework/tool-generated warnings make the project impractical.

---

## 20. Milestones and Automated Completion Gates

A milestone is not complete merely because code exists.

The implementer must execute every applicable **local gate** and only then move on.

If a gate fails:

1. diagnose the failure
2. fix the implementation
3. rerun the failing gate
4. rerun any gates reasonably affected by the fix
5. only then proceed

After all local gates for a milestone pass, create the local milestone checkpoint defined by `AGENTS.md` section 6. Follow `AGENTS.md` for checkpoint subjects/bodies, follow-up `milestone N fix:` commits, branch safety, staging rules, history-rewrite bans, and remote-operation restrictions.

Do not create an empty milestone checkpoint or follow-up-fix commit.

### 20.1 Gate types

- **Local gate**: runs on the developer machine with no Azure write and no GitHub remote write. The implementer runs these autonomously.
- **Azure gate**: requires Azure/Entra state or a deployed environment. Azure gates are accumulated for **Phase V** (section 20.3) and are never run without the user's approval.
- **User gate**: requires the user's interactive action or confirmation (for example manual push/PR creation, browser walkthrough, or second test identity). User gates are accumulated for **Phase V**.

External/user gates do **not** interrupt the local build. From Milestone 0 through Milestone 9, mark them `PENDING-USER` and continue with every independently executable local milestone and gate.

Before Phase V, stop early only when:

- a required local prerequisite is missing or broken and prevents further independent local work
- the current branch/repository state violates `AGENTS.md` safety rules
- continuing would risk overwriting unrelated user work
- a verified fact forces a user-reserved architecture, SKU, cost, or security decision

Milestone 9 marks **local implementation complete** when all local gates pass, even though external gates remain pending. The project is fully complete only after the required Phase V gates pass.

### 20.2 Milestones

#### Milestone 0 — Developer environment

Deliver:

- repository skeleton, including `.gitignore`, `.editorconfig`, `global.json`, `Directory.Build.props`
- prerequisite script
- repo safety script
- versioned pre-commit hook plus `scripts/install-git-hooks.ps1` and `scripts/pre-commit.ps1`
- install/configure the repository hook path locally (`core.hooksPath=.githooks`)
- VS Code recommendations/config foundation
- the owner-provided `LICENSE` in place (do not overwrite)

The pre-commit script must be milestone-aware: at this early stage it runs only checks that are actually available, and later gains build/test/Bicep checks as those artifacts exist.

Local gates:

```powershell
pwsh ./scripts/check-prerequisites.ps1
pwsh ./scripts/check-repo-safety.ps1
```

The prerequisite gate must pass before tool-dependent work continues.

#### Milestone 1 — Solution scaffold

Deliver:

- .NET 10 solution/projects
- shared build configuration
- baseline Blazor app
- baseline Functions app
- baseline tests

Local gates:

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```

All must succeed. Verify the assumption in section 4.2 (HTTP-only Functions host runs without storage).

#### Milestone 2 — Core Todo API and domain behavior

Deliver:

- Todo model, DTOs, and shared validation rules
- service/repository boundary as simple as practical, with an in-memory implementation
- current-user abstraction and the `Dev` identity mode (section 7.2)
- Functions endpoints from section 6.3, ProblemDetails errors
- `docs/openapi.yaml`
- unit tests, including cross-user isolation

Local gates:

- build succeeds
- unit tests succeed
- Functions host starts with the in-memory store for early milestone validation and `/health` returns 200
- endpoint-level HTTP tests pass via `scripts/run-integration-tests.ps1 -Target Local`

#### Milestone 3 — Cosmos persistence and local integration

Deliver:

- Cosmos implementation
- `/userId` partitioning
- local emulator support with the credential handling in section 4.4
- integration tests

Local gates (with `check-prerequisites.ps1 -RequireEmulator` passing):

- Cosmos emulator reachable
- Functions host starts
- CRUD integration tests pass
- cross-user access tests pass (another user's item returns 404)

#### Milestone 4 — Blazor UI

Deliver:

- Interactive Server UI
- Todo workflow
- validation/error states
- API-based data access only
- bUnit tests

Local gates:

- build succeeds
- unit tests (including bUnit) succeed
- Blazor app starts and its root URL responds
- Functions app starts
- local API-plus-UI startup check succeeds

Deferred user gate (Phase V): a local `Dev`-mode browser walkthrough of create, edit, complete, delete, and validation errors.

#### Milestone 5 — Entra authentication and authorization

Deliver:

- tenant-restricted authentication per section 7
- JWT validation middleware in the Functions app
- OIDC sign-in and token acquisition in the Blazor app
- `scripts/setup-entra.ps1`
- protected UI/API
- authorization/user isolation

Local gates:

- JWT validation unit tests pass (wrong issuer, audience, tenant, scope, expired token, missing `oid`)
- `Dev` mode startup guard tests pass
- authorization logic tests pass
- no caller-supplied user ID is trusted (test with a spoofed `userId` in body and query)
- `setup-entra.ps1` parses and passes a read-only dry run where practical (it must not write to Entra without approval)

Azure gates (Phase V): anonymous protected request rejected at APIM and at the Function hostname; authenticated request succeeds; cross-user access returns 404.

#### Milestone 6 — Infrastructure

Deliver:

- Bicep modules
- parameter files
- managed identities/RBAC
- low-cost SKUs
- budget alert
- deployment/teardown scripts

Local gates:

```powershell
az bicep version
az bicep build --file ./infra/main.bicep
az bicep lint --file ./infra/main.bicep
az bicep build-params --file ./infra/parameters/dev.bicepparam
az bicep build-params --file ./infra/parameters/prod.bicepparam
```

Also verify that no template contains `Dev` identity mode, account keys, or shared-key storage access.

Azure gates (Phase V):

- what-if/validation succeeds
- PoC environment deploys successfully
- expected resources exist with the intended SKUs
- rerunning the deployment is idempotent or safely convergent

#### Milestone 7 — Observability

Deliver:

- Application Insights
- Azure Monitor/Log Analytics integration
- structured logs
- fault-injection endpoint (section 11)
- operations documentation/KQL

Local gates:

- structured logging unit tests pass, including a test that tokens and Todo titles do not appear in log output
- the fault endpoint returns 404 by default

Azure gates (Phase V):

- request telemetry visible
- exception test generates expected telemetry
- correlation/trace data can be located
- no sensitive token/secret appears in expected logs

#### Milestone 8 — CI/CD and API validation

Deliver:

- PR workflow
- deployment workflow
- Dependabot configuration
- OIDC configuration documentation
- Postman collection
- automated API validation

Local gates:

- workflow files pass the text-level checks in `check-repo-safety.ps1` and, if `actionlint` happens to be installed, also pass it (it is not required)
- workflow permissions are minimal, and no long-lived Azure secret is referenced
- Postman collection check passes
- local API validation succeeds

Deferred user gates (Phase V): after the user pushes manually and opens/updates the PR, the PR workflow runs green on GitHub; GitHub-side OIDC/environment configuration is confirmed; and the manual deployment workflow is exercised when appropriate. These remain `PENDING-USER` during the local build.

Azure gate (Phase V): deployed smoke/API validation succeeds.

#### Milestone 9 — Final quality and documentation

Deliver:

- completed README; include a single GitHub Actions CI badge for the validation workflow on `main` when a usable GitHub `origin` is configured, otherwise leave the badge as a deferred user/external item without inserting a placeholder
- docs
- ADRs
- security file
- final Git-hook validation
- final cleanup

Local gates:

```powershell
pwsh ./scripts/check-prerequisites.ps1 -RequireEmulator
dotnet restore
dotnet build --no-restore
dotnet test --no-build
pwsh ./scripts/run-integration-tests.ps1 -Target Local
pwsh ./scripts/pre-commit.ps1
pwsh ./scripts/check-repo-safety.ps1
az bicep build --file ./infra/main.bicep
```

Also verify:

- README includes one CI badge for the validation workflow on `main` when a usable GitHub `origin` is available; otherwise the missing badge is explicitly deferred without a placeholder
- README deployment steps match actual scripts
- teardown path is documented
- repository contains no unresolved placeholder TODOs unless explicitly documented
- every Azure and user gate is accounted for by its GitHub/Azure evidence where available or is explicitly listed as still open in the final summary, with no silent gaps

The teardown test (`destroy.ps1` against the deployed PoC environment) requires the user to be present because it needs interactive confirmation.

### 20.3 Phase V — Final external and user validation

Run once, **after Milestone 9 is locally complete**. Do not interrupt Milestones 0–9 to perform these steps while independent local work remains.

Phase V collects every pending user/Azure/GitHub gate from the earlier milestones. Azure or Entra writes still require the user's explicit approval at each approval boundary defined in `AGENTS.md`.

Sequence:

1. perform the deferred local `Dev`-mode browser walkthrough: create, edit, complete, delete, and exercise validation errors
2. if a GitHub remote is configured, confirm the README CI badge uses the real `origin` repository/workflow path; if no remote exists, the user configures it before the badge is added
3. user manually pushes the feature branch and opens/updates the PR to `main`
4. confirm PR CI is green
5. create/configure the dedicated `todo-deployer` Entra application/service principal and its GitHub OIDC federated credential; configure the GitHub Environment/variables. Do **not** assign project-resource-group roles yet if that resource group does not exist
6. user confirms Azure subscription, tenant, region, SKUs, budget email, expected cost, and the planned interactive Entra/resource-group bootstrap
7. perform any required interactive Azure sign-in after explicit approval
8. if the environment is already bootstrapped, `deploy.ps1 -WhatIfOnly` may be run as a zero-write redeployment preview. On a clean first deployment, skip this standalone preview because it cannot create the Entra/resource-group prerequisites
9. for a first deployment, run normal interactive `deploy.ps1`: approve the Entra/resource-group bootstrap, let it create the prerequisites, review the resulting Bicep what-if, then approve infrastructure/application deployment plus Entra finalize
10. confirm smoke tests from the interactive deployment pass
11. now that bootstrap is complete:
    - assign `todo-deployer` Contributor plus Role Based Access Control Administrator at the target resource-group scope
    - set the resulting `todo-api` and `todo-web` client IDs in the required GitHub Environment/repository variables used by `deploy.ps1 -Yes`
    - confirm the remaining GitHub deployment variables (including tenant ID, subscription ID, environment, and other documented non-secret deployment inputs) match the bootstrapped environment
12. obtain `TODOAPP_TOKEN_A` and `TODOAPP_TOKEN_B` and run authenticated Azure integration tests
13. verify anonymous rejection at APIM and at the Function hostname
14. verify **real hosted Entra sign-in through the Blazor app**:
    - sign in as User A through the deployed UI
    - create a Todo and confirm it is visible to User A
    - sign out
    - sign in as User B and confirm User A's Todo is not visible
    - sign back in as User A and delete the Todo
    - where practical, use read-only Cosmos inspection to confirm ownership/partitioning corresponds to User A's `oid` without exposing `userId` through the API
15. verify the deployed Blazor Interactive Server app establishes and maintains its SignalR session on the F1 host within the documented PoC limits
16. temporarily enable fault injection, trigger it, verify telemetry with KQL, then disable it
17. confirm no tokens, secrets, or Todo title/description content appears in expected logs
18. rerun deployment with `deploy.ps1 -Yes` to confirm non-interactive convergence/idempotency; this path must skip all Entra create/finalize operations
19. confirm the `workflow_dispatch` deployment path succeeds with GitHub OIDC using `todo-deployer`; it must deploy without Microsoft Graph application-management permissions
20. tear down when the user chooses; teardown is intentionally optional until the deployed PoC is no longer needed


The hosted browser sign-in gate is required because it exercises the real redirect URIs, `todo-web` app registration, managed-identity-backed `SignedAssertionFromManagedIdentity` credential, delegated API token acquisition, API authorization, and user isolation together.

External verification evidence is kept in the system that performed the check:

- PR/CI results are evidenced by GitHub pull-request and workflow-run history
- deployment results are evidenced by GitHub deployment workflow and/or Azure deployment/activity history
- Application Insights and Azure Monitor provide telemetry evidence
- locally run authenticated Azure integration tests and interactive browser results are reported in the current session summary

Do not create an artificial or empty Git commit solely to record external verification.

If Phase V exposes a defect that requires tracked file changes, use the `milestone N fix:` process in `AGENTS.md` and rerun the affected gates. Any verified Azure fact that changes or constrains the design must be copied into the relevant tracked documentation or ADR.

---

## 21. Definition of Done

The project is done when:

- a developer can clone the repository on Windows 11
- prerequisites are clearly documented
- `scripts/check-prerequisites.ps1` accurately validates the machine
- the solution builds on .NET 10
- unit tests pass
- local Functions run with Azure Functions Core Tools
- local persistence works with Cosmos DB Emulator
- the Blazor Interactive Server UI works
- the UI accesses data only through the API
- per-user authorization is enforced, and another user's item is indistinguishable from a missing one
- Azure infrastructure is reproducibly deployed through Bicep
- serverless, consumption, and free SKUs are used wherever practical, with the tradeoffs documented
- APIM fronts the Azure Functions API
- Entra protects access
- Cosmos access uses managed identity/RBAC, with key authentication disabled
- Application Insights and Azure Monitor provide useful telemetry
- Postman/API validation artifacts are included
- GitHub Actions use OIDC rather than a long-lived Azure credential
- the project can be torn down safely
- README and supporting docs accurately describe the real implementation
- Milestones 0–9 have all applicable local gates passing and successful local checkpoint commits where file changes occurred
- required Phase V user/Azure/GitHub gates have passed; teardown may remain intentionally deferred while the PoC is being retained
- there are no committed credentials, and the Cosmos emulator key is not committed
- the solution remains intentionally simple

Milestone 9 completion means the repository is **locally complete and ready for external validation**. Do not describe the entire project as fully complete until the required Phase V validation is finished.

---

## 22. Assumptions to Verify

Check each against current Microsoft documentation in the milestone shown. Put durable results that affect the final design into the relevant tracked documentation or ADR.

If a verification is completed before the milestone checkpoint commit, include the result in that commit body. If it is completed later during the post-Milestone-9 Phase V, CI, or another external/user verification step, do not create or rewrite a commit solely to record it; report it in the final summary and update tracked documentation or an ADR when it affects the design.

| Milestone | Assumption |
| --- | --- |
| 1 | HTTP-only Functions host runs locally without a storage account. |
| 3 | The emulator supports the container settings used locally. |
| 5 | The Azure CLI's first-party client can be pre-authorized on `todo-api` to obtain test tokens for the delegated API scope. |
| 6 | Flex Consumption supports .NET 10 isolated in the chosen region, and the intended scale/maximum-instance settings are valid there. |
| 6 | APIM Consumption supports the chosen JWT-validation policy and OpenAPI import path used by the implementation. |
| 6 | The selected Cosmos API/Bicep versions support serverless deployment, `disableLocalAuth`, and the required data-plane role assignment exactly as authored. |
| 6 | Linux App Service Free (F1) remains available in the chosen region with quotas suitable for this PoC; re-check its current WebSocket and daily compute limits before deployment. |
| 7 | Log Analytics daily cap minimum and current free ingestion allowance. |
