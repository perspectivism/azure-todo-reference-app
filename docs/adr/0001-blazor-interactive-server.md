# ADR 0001: Blazor Interactive Server instead of WebAssembly

Status: Accepted

## Context

The UI is a small authenticated Todo app that must call the API only through APIM with a delegated access token, and must not expose data access to the browser. Options considered: Blazor Web App with Interactive Server rendering, or Blazor WebAssembly (standalone or hosted).

## Decision

Use a .NET 10 Blazor Web App with Interactive Server render mode. The server signs users in with OpenID Connect (confidential client), acquires delegated tokens with Microsoft.Identity.Web, and calls the API server-to-server.

## Alternatives

- **Blazor WebAssembly**: the browser would hold access tokens and call APIM directly, which requires CORS on APIM, a public client registration, and token handling in the browser. The download size is also larger.

## Consequences

- Tokens stay on the server; no CORS configuration is needed; the web app uses a secretless confidential-client credential (federated credential via managed identity).
- Each active user holds a SignalR circuit. Linux App Service Free (F1) allows 5 concurrent WebSockets and 60 CPU minutes per day (verified in Milestone 6), which limits the PoC to a few simultaneous users. B1 is available by explicit choice.
- The token cache is in memory: after an app restart users sign in again (the session cookie is rejected when the cached account is missing).
- The home page is prerendered on the server, so the first HTML response already contains data from the API; this also enables a browser-free startup check in `run-integration-tests.ps1`.
