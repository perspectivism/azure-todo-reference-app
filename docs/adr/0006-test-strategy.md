# ADR 0006: Test strategy

Status: Accepted

## Context

The project needs repeatable validation of API behaviour, authorization, UI states, and deployments on Windows without a separately installed Node.js toolchain, Docker, or browser automation.

## Decision

- **xUnit is the authoritative suite.**
  - `tests/Todo.UnitTests` (xUnit v3 + bUnit): validation, service behaviour, two-user isolation, JWT rules with locally generated signing keys (no network), Dev-mode startup guard, spoofed `userId` handling, logging content, authentication and error-mapping middleware decisions, the OpenAPI contract matching the function routes, Blazor component states with a fake API client. Fast and hermetic; run in the pre-commit hook and CI.
  - `tests/Todo.IntegrationTests` (xUnit v3): HTTP tests against a running API, tagged `Target=Local` and/or `Target=Azure`, selected with `dotnet test --filter`, and skipped when `TODOAPP_API_BASE_URL` is unset. `scripts/run-integration-tests.ps1 -Target Local` runs them against the Functions host backed by the Cosmos DB Emulator in two phases (Dev mode for CRUD/isolation, Entra mode for 401 checks) and adds a Blazor startup check.
- **Postman is a convenience**: `postman/todo-api.postman_collection.json` covers every route for manual exploration. It is validated statically (valid JSON, every route present) by `scripts/check-repo-safety.ps1`, not executed as a gate.
- **Smoke tests**: `scripts/smoke-test.ps1` runs after every deployment (`/health`, anonymous rejection, and create/read/delete when a token is supplied).
- **UI**: bUnit plus the automated startup check; a full browser walkthrough is a manual Phase V step.

## Alternatives

- **Postman/Newman as the API gate**: requires Node.js.
- **Playwright or Selenium for UI tests**: adds tooling and maintenance disproportionate to this PoC.
- **In-process WebApplicationFactory for Functions**: not available for the Functions host; the real `func` host is used instead.

## Consequences

- The test projects use the VSTest runner (`UseMicrosoftTestingPlatformRunner=false`) so the documented `dotnet test --filter "Target=Local"` syntax works with the .NET 10 SDK.
- CI cannot run the Cosmos-backed integration tests or authenticated Azure tests (no emulator, no user tokens); those run locally and in Phase V.
