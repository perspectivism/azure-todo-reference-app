# ADR 0005: Authentication design

Status: Accepted

## Context

The app needs Entra sign-in for users, delegated API access, a way to develop and test locally without Entra, a way for automated Azure tests to act as two users, and no stored secrets.

## Decision

1. **Separate registrations**: `todo-api-<env>` exposes `access_as_user` (v2 tokens); `todo-web-<env>` signs users in and requests that scope. Both are single-tenant and created/finalized by `scripts/setup-entra.ps1` during an interactive, user-authorized deployment, not by Bicep or CI.
2. **Secretless web credential**: `todo-web` trusts a federated identity credential whose subject is the App Service's user-assigned managed identity; the app uses Microsoft.Identity.Web 4.15.0 with `SignedAssertionFromManagedIdentity`. No client secret is created.
3. **Dev identity mode**: `Auth:Mode=Dev` (Development only; startup throws elsewhere). The API accepts an `X-Dev-User-Id` GUID header (default: a fixed development user) so integration tests can act as two users; the web app signs in a fixed development user.
4. **Test tokens**: automated Azure tests read `TODOAPP_TOKEN_A`/`TODOAPP_TOKEN_B` from the environment, obtained with `az account get-access-token --scope api://<todo-api>/access_as_user` (second user through a separate `AZURE_CONFIG_DIR`). `todo-api` pre-authorizes the Azure CLI client (`04b07795-8ddb-461a-bbee-02f9e1bf7b46`) for the scope.
5. **Deployment identity**: `todo-deployer` (application/service principal with a GitHub OIDC federated credential) has Azure RBAC on the resource group only and no Graph permissions.

## Alternatives

- **Client secret or certificate for `todo-web`**: requires rotation and secure storage (for example Key Vault); rejected because a secretless option exists.
- **Managing registrations in Bicep (Microsoft Graph Bicep extension)**: would require the deployment identity, including CI, to hold Graph application-management permissions.
- **Mocked authentication only for local tests**: would not let HTTP tests exercise two identities against the real host; Dev mode keeps a real authentication handler in the pipeline.
- **ROPC or client-credential tokens for tests**: ROPC is discouraged and fails with MFA; app-only tokens have no `scp` or user `oid`.

## Verified facts

- Milestone 5 (Microsoft Learn): any client application id can be pre-authorized under "Authorized client applications" so users are not prompted for consent; the Azure CLI's application id is `04b07795-8ddb-461a-bbee-02f9e1bf7b46`. Obtaining a token this way is confirmed in Phase V.
- Microsoft.Identity.Web 4.x no longer ships `RejectSessionCookieWhenAccountNotInCacheEvents`; the equivalent cookie validation is implemented in `EntraWebAuthentication`.

## Consequences

- CI can redeploy but never create or repair Entra configuration; changes to redirect URIs or the managed identity require an interactive deployment.
- User consent for sign-in scopes (`openid`, `profile`, `offline_access`) may be required on first sign-in, depending on tenant policy.
- Two real users in the tenant are needed for the Azure user-isolation check; without the second token the test reports skipped and the gate stays open.
