# ADR 0003: Cosmos DB serverless instead of the free tier

Status: Accepted

## Context

Todos are stored in Azure Cosmos DB for NoSQL, partitioned by the owner's Entra object id (`/userId`). Traffic is intermittent. Cosmos DB offers a free tier (a fixed amount of provisioned throughput and storage for one account per subscription) and a serverless capacity mode.

## Decision

Use a serverless account. Bicep creates database `todo` and container `todos` (partition key `/userId`, no throughput settings). Key-based authentication is disabled; the Function App's managed identity has the built-in Data Contributor data-plane role scoped to the `todo` database.

## Alternatives

- **Free tier (provisioned throughput)**: limited to one account per subscription, and not available for serverless accounts. Deploying `dev` and `prod` in one subscription would exceed it, and provisioned throughput above the free allowance bills continuously.
- **Provisioned or autoscale throughput without free tier**: billed per hour regardless of use.

## Verified facts

- Milestone 6: the Bicep types for `Microsoft.DocumentDB` API version 2025-04-15 (stable; latest stable 2026-03-15) accept `EnableServerless`, `disableLocalAuth`, and `sqlRoleAssignments` scoped to a database path; the template builds and lints without warnings.
- Milestone 3: the Cosmos DB Emulator supports the container settings used locally (partition key `/userId`, default indexing, created without explicit throughput); the emulator provisions throughput because it does not support serverless.

## Consequences

- Cost is per request unit consumed and per GB stored; idle cost is storage only. The cost is small but not zero.
- Serverless behaviour (for example request-unit limits per operation) is only exercised in Azure, not locally.
- Entra data-plane access cannot create databases or containers, so Bicep owns them; application auto-creation is enabled only in local development.
