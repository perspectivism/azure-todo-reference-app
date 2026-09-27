# Security

## Authentication

- Users sign in to the Blazor app with Microsoft Entra ID (OpenID Connect, `todo-web-<env>`, single tenant). Only accounts in the configured tenant can sign in.
- The Blazor app calls the API server-to-server with a delegated v2 access token for `api://<todo-api>/access_as_user`, acquired with Microsoft.Identity.Web and cached in memory. Tokens are never shown in the UI or logged.
- The web app's own client credential is a federated identity credential backed by its user-assigned managed identity (`SignedAssertionFromManagedIdentity`). No client secret exists in Azure.
- Local development uses `Auth:Mode=Dev` (see "Dev identity mode").

## Authorization

- Every API route except `GET /health` requires a valid token. The Function uses `AuthorizationLevel.Anonymous` because function keys are shared credentials; the bearer token is the gate, validated by ASP.NET Core authentication in worker middleware.
- Token rules, enforced by both APIM and the Function: issuer is the configured tenant (v2 endpoint), audience is the `todo-api` application (`<client id>` or `api://<client id>`), lifetime is valid (2-minute clock skew), signature validates against the tenant's published keys, `tid` equals the configured tenant, `scp` contains `access_as_user`, and `oid` is present.
- The owner of every todo is the token's `oid`. The API never accepts a user id from the URL, query, or body. Queries and point reads are scoped to the caller's partition (`/userId`), and another user's todo returns 404 like a missing one.
- `POST /diagnostics/fault` also requires a token and returns 404 unless explicitly enabled.

## Why APIM and the Function both validate tokens

APIM Consumption has no fixed outbound IP address, so the Function App cannot be restricted to traffic from APIM by IP, and the Function's own `*.azurewebsites.net` hostname stays publicly reachable. APIM validation rejects unauthenticated traffic at the gateway (before it reaches compute) and gives a single place to see gateway errors. The Function validates the same rules so that calling its hostname directly gains nothing, and so that authorization logic is unit-testable and works locally without APIM. Deployed smoke tests check anonymous rejection at both hostnames.

## Dev identity mode

- `Auth:Mode=Dev` exists only for local development: the API accepts an `X-Dev-User-Id` GUID header (falling back to a fixed development user) and the web app signs in a fixed development user.
- It is set only in `appsettings.Development.json`, which is excluded from publish. Both apps throw at startup if `Auth:Mode` is `Dev` and the host environment is not Development. Unit tests cover this guard.
- `scripts/check-repo-safety.ps1` fails if a `'Dev'` value or the `X-Dev-User-Id` header appears in `infra/`. In Entra mode, the Dev header is ignored (covered by tests).

## Secret handling

- No credentials are committed. The repository contains no connection strings with keys, client secrets, tokens, or private keys; `check-repo-safety.ps1` checks for common patterns on every commit and in CI.
- The Cosmos DB Emulator key is public but is still never committed: developers copy it from Microsoft's documentation into the untracked `local.settings.json`. The safety script detects the key by SHA-256 hash.
- Tenant, subscription, and client ids are not secrets, but they are supplied through environment variables, script parameters, or GitHub variables.
- GitHub Actions authenticates to Azure with OIDC; no Azure credential is stored in GitHub.
- Test tokens (`TODOAPP_TOKEN_A/B`) live only in environment variables of the shell that runs the tests.
- Key Vault is not used because no application secret exists.

## Managed identities and RBAC

| Principal | Role | Scope |
| --- | --- | --- |
| Function App (system-assigned) | Cosmos DB Built-in Data Contributor (data plane) | `todo` database |
| Function App (system-assigned) | Storage Blob Data Owner | Function storage account (host storage and deployment container) |
| Web app (user-assigned) | none in Azure RBAC | Trusted only by the `todo-web` federated credential |
| `todo-deployer` | Contributor, Role Based Access Control Administrator | Environment resource group only |

Cosmos DB key-based authentication is disabled (`disableLocalAuth`), and so is key-based metadata write access. The storage account disallows shared-key access and public blob access and requires TLS 1.2. Basic publishing credentials (SCM/FTP) are disabled on both apps; deployments use Entra-authenticated calls.

## Input validation

- Titles are required, trimmed, 1 to 200 characters; descriptions are optional, at most 2000 characters. The limits live in `Todo.Contracts` and are shared with the UI.
- Request bodies larger than 16 KB are rejected with 413 before deserialization; the body read is bounded even without `Content-Length`.
- Malformed JSON, wrong JSON types, invalid paging parameters, and invalid continuation tokens return 400. Malformed ids return 404.
- Unknown JSON properties are ignored, so clients cannot set `userId`, `id`, or timestamps.
- Errors use RFC 9457 problem responses without stack traces or internal details.
- Blazor renders user content with Razor encoding; no raw HTML is rendered. Sign-in return URLs are restricted to local paths (open-redirect protection). Sign-out is a POST with an antiforgery token.

## Logging considerations

- Logs contain ids only (`TodoId`, `UserId` = `oid`, function name). Todo titles and descriptions are never logged; the Authorization header and tokens are never logged (unit tests check both, including JwtBearer failure logs).
- APIM diagnostics log no headers and no bodies, and do not log client IPs.
- `docs/operations.md` includes a KQL spot check for token fragments.

## Dependency and platform assumptions

- Dependencies come from NuGet and are kept current by Dependabot; CI reports vulnerable packages (`dotnet list package --vulnerable --include-transitive`).
- GitHub Actions are pinned to release tags and updated by Dependabot; workflows use least-privilege `permissions` and never use `pull_request_target`.
- The design relies on Microsoft Entra ID token issuance and signing-key publication, APIM `validate-jwt`, App Service/Functions TLS termination, and Azure RBAC behaving as documented.
- Rate limiting by key is not available on APIM Consumption; see `docs/threat-model.md` for the residual risk.

See also `docs/threat-model.md` and the repository `SECURITY.md`.
