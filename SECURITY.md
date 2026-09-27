# Security Policy

## Supported scope and assumptions

This repository is a proof-of-concept reference application. Only the latest commit on `main` is maintained. It assumes:

- a single Microsoft Entra ID tenant, with app registrations created by `scripts/setup-entra.ps1`;
- Azure platform services (Entra ID, API Management, App Service, Azure Functions, Cosmos DB, Azure Monitor) behaving as documented;
- deployments performed by `scripts/deploy.ps1` or the GitHub `deploy` workflow with OIDC.

Design details are in `docs/security.md` and `docs/threat-model.md`.

## Reporting a vulnerability

Do not open a public issue for a vulnerability. Use GitHub's private vulnerability reporting for this repository (**Security**, **Report a vulnerability**) if it is enabled, or contact the repository owner privately. Include:

- a description of the issue and its impact;
- steps to reproduce against a local environment (`Auth:Mode=Dev`) where possible;
- affected files, commits, or configuration.

Expect an acknowledgement within a reasonable time; this is a PoC maintained on a best-effort basis.

## Secret handling expectations

- Never commit credentials: client secrets, access or refresh tokens, storage or Cosmos keys, connection strings with keys, certificates with private keys, or the Cosmos DB Emulator key (copy it from Microsoft's documentation into the untracked `local.settings.json`).
- Tenant, subscription, and client ids are not secrets but are supplied through configuration, not committed.
- Install the pre-commit hook (`pwsh ./scripts/install-git-hooks.ps1`); it runs `scripts/check-repo-safety.ps1`. CI runs the same checks.
- If a secret is committed, treat it as compromised: revoke or rotate it first, then remove it from history.

## What not to include in issues, pull requests, or logs

- access tokens (anything starting with `eyJ`), `Authorization` headers, cookies;
- connection strings, keys, client secrets, publishing profiles;
- personal data from real users, including todo content;
- full HTTP captures from an authenticated session.

Redact these before sharing screenshots, logs, or Postman exports.
