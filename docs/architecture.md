# Architecture

## Logical architecture

```text
Browser
  |  HTTPS + SignalR (Blazor Interactive Server circuit)
  v
Blazor Web App (Todo.Web)            Azure App Service, Linux F1, user-assigned managed identity
  |  HTTPS + delegated bearer token (server-to-server, no CORS)
  v
Azure API Management (Consumption)   validate-jwt on every operation except GET /health
  |  HTTPS + the same bearer token
  v
Azure Functions (Todo.Api)           Flex Consumption, .NET 10 isolated worker, system-assigned managed identity
  |  Microsoft Entra ID (managed identity) + Cosmos DB data-plane RBAC
  v
Azure Cosmos DB for NoSQL            serverless, database todo, container todos, partition key /userId

Cross-cutting: Microsoft Entra ID, Application Insights + Log Analytics, Bicep, GitHub Actions + OIDC
```

## Components

| Component | Project / resource | Responsibility |
| --- | --- | --- |
| Shared contracts | `src/Todo.Contracts` | Request/response DTOs, validation limits (`TodoLimits`), `TodoValidator`, Dev-mode identity constants. Referenced by the API and the web app. |
| API | `src/Todo.Api` | Functions host. Thin HTTP functions (`TodoFunctions`, `HealthFunction`, `DiagnosticsFunctions`) delegate to `TodoService`, which depends on `ITodoRepository` (`CosmosTodoRepository`; `InMemoryTodoRepository` for tests and early local validation). Worker middleware authenticates every non-health request (`AuthenticationMiddleware`) and maps failures to RFC 9457 problem responses (`ProblemHandlingMiddleware`). |
| Web | `src/Todo.Web` | Blazor Web App, Interactive Server. `TodoBoard` renders the list and forms and calls `ITodoApiClient`, a typed `HttpClient`. `ITodoApiRequestAuthorizer` attaches the user's identity per request. The web app has no Cosmos DB access. |
| Gateway | API Management, Consumption | Imports `docs/openapi.yaml`; validates access tokens; forwards to the Function App; logs to Application Insights with W3C correlation. No subscription key. |
| Data | Cosmos DB serverless | Stores todos. Key-based auth disabled; the database and container are created by Bicep. |
| Telemetry | Application Insights (workspace-based) + Log Analytics | Requests, dependencies, exceptions, and logs from all three tiers; daily ingestion cap. |
| Infrastructure | `infra/` | Bicep for all Azure resources; `scripts/deploy.ps1` creates the resource group and orchestrates deployment. |

## Main request flow (list todos)

1. The browser holds a SignalR circuit to the Blazor app. The Blazor app knows the signed-in user from its session cookie (OpenID Connect sign-in against `todo-web`).
2. `TodoBoard` calls `ITodoApiClient.ListAsync`. `EntraTodoApiRequestAuthorizer` gets a delegated access token for `api://<todo-api>/access_as_user` from the in-memory token cache (Microsoft.Identity.Web) and sets the `Authorization` header.
3. APIM `validate-jwt` checks issuer, audience, lifetime, signature (tenant OpenID metadata), `tid`, `scp`, and `oid`. Invalid or missing tokens get 401 at the gateway.
4. The Function's JwtBearer handler validates the same rules again. The caller's `oid` becomes `CurrentUser.UserId`.
5. `TodoService` queries Cosmos DB in the caller's partition (`/userId = oid`) with `WHERE c.userId = @userId ORDER BY c.createdUtc DESC`.
6. The response (`items`, `continuationToken`) flows back; the owner id is never included.

## Authentication and deployment identities

| Identity | Kind | Used by | Used for |
| --- | --- | --- | --- |
| `todo-api-<env>` | Entra app registration | APIM and the Function (as token audience) | Exposes delegated scope `access_as_user`; issues v2 access tokens. Pre-authorizes `todo-web-<env>` and Azure CLI. |
| `todo-web-<env>` | Entra app registration | Blazor app | Signs users in (OIDC) and acquires delegated tokens for `todo-api`. Its client credential is a federated credential, not a secret. |
| `<prefix>-<env>-web-id` | User-assigned managed identity on the App Service | Blazor app | Produces the assertion (`SignedAssertionFromManagedIdentity`) that `todo-web` trusts through its federated identity credential (issuer = tenant, subject = identity principal id, audience `api://AzureADTokenExchange`). |
| Function App system-assigned identity | Managed identity | Function App | Cosmos DB data-plane access (built-in Data Contributor on the `todo` database) and identity-based host storage (Storage Blob Data Owner). |
| `todo-deployer` | Entra application/service principal with a GitHub OIDC federated credential | GitHub `deploy` workflow only | Azure RBAC on the environment resource group (Contributor + Role Based Access Control Administrator). No Microsoft Graph permissions; never modifies `todo-api`/`todo-web`. Not used at runtime. |

`todo-api` and `todo-web` are created and finalized by `scripts/setup-entra.ps1` during an interactive, user-authorized deployment; they are not in Bicep.

## Data ownership model

- Each todo stores `userId` = the Entra object id (`oid`) of its owner. It is the Cosmos partition key.
- `userId` is taken only from the validated token (or the Dev-mode header locally). Request bodies and query strings cannot set it; any `userId`, `id`, or timestamp field in a body is ignored.
- Point reads use `id` plus the caller's partition key; queries use the caller's partition key and filter on `userId`.
- Another user's todo returns 404, the same as a missing todo, for read, update, and delete.
- `userId` is never returned by the API.

## Observability flow

- The Blazor app uses the Azure Monitor OpenTelemetry distro; the Function App uses OpenTelemetry with the Azure Monitor exporter (`telemetryMode: OpenTelemetry` in `host.json`); APIM uses an Application Insights logger with W3C correlation.
- W3C trace context propagates from the Blazor app's outgoing HTTP call through APIM to the Function, so a single operation id links all three. API error responses include `traceId`.
- Logs are structured (`TodoId`, `UserId`, `FunctionName`); tokens and todo content are never logged. See `docs/operations.md` for KQL.

## Local development differences

| Concern | Local | Azure |
| --- | --- | --- |
| Identity | `Auth:Mode=Dev` (`appsettings.Development.json`, Development only). API accepts `X-Dev-User-Id`; web signs in a fixed development user. | `Auth:Mode=Entra`; Dev mode is rejected at startup outside Development and never appears in infrastructure. |
| Gateway | None: the web app calls the Functions host directly (`http://localhost:7071`). | APIM Consumption. |
| Cosmos DB | Cosmos DB Emulator with a connection string from untracked configuration; database/container auto-created (`Cosmos:AutoCreate`, Development only). The emulator uses provisioned throughput, so serverless behaviour is only exercised in Azure. | Serverless account, managed identity, key auth disabled; Bicep creates the database and container. |
| Host storage | None: the HTTP-only Functions host runs without a storage account (verified). | Identity-based storage account. |
