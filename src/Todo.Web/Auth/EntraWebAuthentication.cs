using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Todo.Web.Api;

namespace Todo.Web.Auth;

/// <summary>
/// Entra mode: OpenID Connect sign-in for the todo-web registration and delegated token acquisition for the todo-api
/// scope. The app's client credential is configured under AzureAd:ClientCredentials; in Azure it is
/// SignedAssertionFromManagedIdentity (a federated credential backed by the App Service's user-assigned managed
/// identity), so no client secret exists. Tokens are cached in memory: after an app restart users sign in again.
/// </summary>
public static class EntraWebAuthentication
{
    public const string AzureAdSection = "AzureAd";

    public static void Add(IServiceCollection services, IConfiguration configuration, TodoApiOptions apiOptions)
    {
        string[] scopes = [apiOptions.Scope!];

        services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApp(configuration.GetSection(AzureAdSection))
            .EnableTokenAcquisitionToCallDownstreamApi(scopes)
            .AddInMemoryTokenCaches();

        services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            // Keep raw claim names (oid, name, preferred_username).
            options.MapInboundClaims = false;
            options.TokenValidationParameters.NameClaimType = WebClaims.Name;
        });

        services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Events.OnValidatePrincipal = context => RejectSessionWithoutCachedAccountAsync(context, scopes);
        });

        services.AddScoped<ITodoApiRequestAuthorizer, EntraTodoApiRequestAuthorizer>();
    }

    /// <summary>
    /// The token cache is in memory. When the app restarts, the session cookie survives but the cached account does not,
    /// so reject the cookie and let the user sign in again instead of failing on the first API call.
    /// </summary>
    private static async Task RejectSessionWithoutCachedAccountAsync(CookieValidatePrincipalContext context, string[] scopes)
    {
        if (context.Principal is null)
        {
            return;
        }

        var headerProvider = context.HttpContext.RequestServices.GetRequiredService<IAuthorizationHeaderProvider>();
        try
        {
            await headerProvider.CreateAuthorizationHeaderForUserAsync(scopes, claimsPrincipal: context.Principal, cancellationToken: context.HttpContext.RequestAborted);
        }
        catch (Exception ex) when (RequiresSignIn(ex))
        {
            context.RejectPrincipal();
        }
    }

    internal static bool RequiresSignIn(Exception ex)
        => ex is MicrosoftIdentityWebChallengeUserException or MsalUiRequiredException
            || ex.InnerException is MsalUiRequiredException;
}

/// <summary>Entra mode: attaches the signed-in user's delegated access token for the todo-api scope. The token is never logged.</summary>
public sealed class EntraTodoApiRequestAuthorizer(
    AuthenticationStateProvider authenticationStateProvider,
    IAuthorizationHeaderProvider authorizationHeaderProvider,
    TodoApiOptions apiOptions) : ITodoApiRequestAuthorizer
{
    public async Task AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ClaimsPrincipal user = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true)
        {
            throw new ReauthenticationRequiredException();
        }

        try
        {
            var header = await authorizationHeaderProvider.CreateAuthorizationHeaderForUserAsync(
                [apiOptions.Scope!], claimsPrincipal: user, cancellationToken: cancellationToken);
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(header);
        }
        catch (Exception ex) when (EntraWebAuthentication.RequiresSignIn(ex))
        {
            throw new ReauthenticationRequiredException(ex);
        }
    }
}
