using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Todo.Api.Auth;

/// <summary>Claim types used by the API. Inbound claim mapping is disabled, so these are the raw JWT names.</summary>
public static class EntraClaims
{
    public const string ObjectId = "oid";
    public const string TenantId = "tid";
    public const string Scope = "scp";
    public const string Name = "name";
}

/// <summary>
/// Access-token rules enforced by the Function (the same rules APIM applies in validate-jwt):
/// issuer is the configured tenant, audience is the todo-api application, lifetime is valid,
/// tid matches the tenant, scp contains the required scope, and oid is present.
/// </summary>
public static class EntraTokenValidation
{
    public static TokenValidationParameters CreateParameters(AuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudiences = [options.ClientId, $"api://{options.ClientId}"],
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            NameClaimType = EntraClaims.Name,
        };
    }

    /// <summary>Returns null when the validated principal satisfies the claim rules, otherwise the failure reason.</summary>
    public static string? GetClaimFailure(ClaimsPrincipal? principal, AuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (principal is null)
        {
            return "No principal.";
        }

        var tenantId = principal.FindFirst(EntraClaims.TenantId)?.Value;
        if (!string.Equals(tenantId, options.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            return "Token tenant (tid) does not match the configured tenant.";
        }

        var scopes = principal.FindFirst(EntraClaims.Scope)?.Value?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (!scopes.Contains(options.RequiredScope, StringComparer.Ordinal))
        {
            return $"Token scope (scp) does not contain '{options.RequiredScope}'.";
        }

        if (string.IsNullOrWhiteSpace(principal.FindFirst(EntraClaims.ObjectId)?.Value))
        {
            return "Token has no object id (oid) claim.";
        }

        return null;
    }
}
