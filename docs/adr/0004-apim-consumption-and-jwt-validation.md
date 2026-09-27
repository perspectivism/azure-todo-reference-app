# ADR 0004: API Management Consumption tier and where tokens are validated

Status: Accepted

## Context

The specification places API Management in front of the Function App. Tiers differ greatly in cost and features. The Function App's hostname is public.

## Decision

- Use APIM **Consumption** (usage-based). Import the API from `docs/openapi.yaml` with an empty URL suffix so gateway routes match the Function routes. No subscription key is required.
- Validate access tokens in **both** places: APIM `validate-jwt` (tenant v2 OpenID metadata, audience, issuer, `tid`, `scp` contains `access_as_user`, plus an `oid` presence check) on every operation except `GET /health`, and ASP.NET Core JwtBearer authentication in the Function with the same rules.

## Alternatives

- **Developer/Basic/Standard/Premium APIM**: fixed hourly cost; Developer has no SLA and is still billed continuously.
- **Validate only in APIM**: APIM Consumption has no fixed outbound IP, so the Function cannot be restricted to APIM; its hostname would accept unauthenticated calls.
- **Validate only in the Function**: unauthenticated traffic would reach compute, and the gateway would add no protection.
- **Subscription keys and `rate-limit-by-key`**: per-key rate limiting is not available on Consumption, and distributing keys conflicts with the secretless design.
- **Function keys**: shared secrets; not used (`AuthorizationLevel.Anonymous`, the JWT is the gate).

## Verified facts (Milestone 6, Microsoft Learn)

- `validate-jwt` applies to the Consumption gateway and supports `openid-config`, audiences, issuers, and required claims with a separator (used for the space-delimited `scp`).
- `Microsoft.ApiManagement/service/apis` accepts inline OpenAPI 3.0 YAML (`format: 'openapi'`), and `path` has only a maximum length, so an empty suffix is valid.

## Consequences

- Authorization logic is unit-testable and works locally without APIM.
- Residual risks (no per-key rate limit, public Function hostname) are documented in `docs/threat-model.md`; the Function's maximum instance count and the budget alert bound abuse cost.
- The OpenAPI file is hand-maintained and is the source for the APIM import. A unit test (`OpenApiContractTests`) fails if its paths and methods differ from the HTTP-triggered functions, or if `GET /health` is not the `getHealth` operation that the APIM health policy targets.
