namespace Todo.Web.Auth;

/// <summary>Sign-in and sign-out endpoints used by the layout's sign-in/sign-out controls.</summary>
public static class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/authentication");

        group.MapGet("/sign-in", (string? returnUrl, WebAuthOptions options) =>
            // The development user is always signed in.
            Results.LocalRedirect(SafeReturnUrl(returnUrl)))
            .AllowAnonymous();

        group.MapPost("/sign-out", (WebAuthOptions options) =>
            // The fixed development user cannot sign out.
            Results.LocalRedirect("~/"));

        return endpoints;
    }

    /// <summary>Only local paths are accepted as return URLs (prevents open redirects).</summary>
    internal static string SafeReturnUrl(string? returnUrl)
        => !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal) && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : "/";
}
