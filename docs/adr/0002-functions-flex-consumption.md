# ADR 0002: Azure Functions on Flex Consumption

Status: Accepted

## Context

The API runs as Azure Functions (.NET isolated worker) on .NET 10 with low, intermittent traffic. It must scale to zero, avoid storage account keys, and keep cost bounded.

## Decision

Host the Function App on the Flex Consumption plan (Linux, FC1) with zero always-ready instances, a maximum instance count of 1, and 2048 MB instances. Host storage and the deployment container use the Function App's managed identity; shared-key access on the storage account is disabled.

## Alternatives

- **Linux Consumption**: does not support .NET 10 and is being retired.
- **Windows Consumption**: legacy plan without the Flex deployment model or identity-based deployment storage configuration used here.
- **Premium (Elastic Premium) or Dedicated**: always-on billing that does not fit an intermittent PoC.

## Verified facts (Milestone 6, Microsoft Learn)

- Flex Consumption supports C# isolated worker on .NET 8, 9, and 10.
- The lowest maximum instance count is 1 (highest 1000); always-ready defaults to 0.
- Instance sizes are 512, 2048, and 4096 MB; 2048 MB is Microsoft's recommended default.
- On-demand execution has a monthly free grant; always-ready instances have no free grant.
- Regional availability varies; `deploy.ps1` checks the chosen region read-only and fails instead of substituting a plan.
- Locally, the HTTP-only Functions host runs without any storage account (verified in Milestone 1), so Azurite is not needed.

## Consequences

- Cold starts occur after idle periods; smoke tests retry `/health`.
- Maximum 1 instance caps both throughput and the cost of abuse; raise `functionMaximumInstanceCount` if needed.
- One app per Flex plan; no deployment slots (rollback is a redeploy of a previous build).
- 512 MB instances would cost less per execution second but have 0.25 vCPU, which risks slow .NET cold starts against the 30-second host initialization timeout; 2048 MB was chosen.
