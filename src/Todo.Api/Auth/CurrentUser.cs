using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Todo.Api.Auth;

/// <summary>
/// The authenticated caller for the current function invocation (scoped). Set by <see cref="AuthenticationMiddleware"/>
/// from the validated identity's oid claim. There is no way to set it from request data.
/// </summary>
public sealed class CurrentUser
{
    private string? _userId;

    public string UserId => _userId ?? throw new InvalidOperationException("No authenticated user for this invocation.");

    internal void Set(string userId) => _userId = userId;

    /// <summary>
    /// Authenticates the request with the configured default scheme (JwtBearer in Entra mode, Dev in Dev mode).
    /// Returns the caller's object id, or null when the request has no valid identity.
    /// </summary>
    public static async Task<string?> AuthenticateAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var result = await httpContext.AuthenticateAsync();
        if (!result.Succeeded)
        {
            return null;
        }

        var objectId = result.Principal.FindFirst(EntraClaims.ObjectId)?.Value;
        if (string.IsNullOrWhiteSpace(objectId))
        {
            return null;
        }

        httpContext.User = result.Principal;
        return objectId;
    }
}
