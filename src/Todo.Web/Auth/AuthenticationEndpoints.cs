using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Todo.Web.Auth;

/// <summary>Sign-in and sign-out endpoints used by the layout's sign-in/sign-out controls.</summary>
public static class AuthenticationEndpoints
{
    public const string SignedOutPath = "/signed-out";

    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/authentication");

        group.MapGet("/sign-in", (string? returnUrl, WebAuthOptions options) =>
            options.Mode == WebAuthMode.Entra
                ? Results.Challenge(
                    new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) },
                    [OpenIdConnectDefaults.AuthenticationScheme])
                // The development user is always signed in.
                : Results.LocalRedirect(SafeReturnUrl(returnUrl)))
            .AllowAnonymous();

        // POST with the antiforgery token rendered by the sign-out form.
        group.MapPost("/sign-out", (WebAuthOptions options) =>
            options.Mode == WebAuthMode.Entra
                ? Results.SignOut(
                    new AuthenticationProperties { RedirectUri = SignedOutPath },
                    [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme])
                // The fixed development user cannot sign out.
                : Results.LocalRedirect("~/"));

        return endpoints;
    }

    /// <summary>Only local paths are accepted as return URLs (prevents open redirects).</summary>
    internal static string SafeReturnUrl(string? returnUrl)
        => !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal) && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : "/";
}
