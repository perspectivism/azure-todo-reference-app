using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using Todo.Contracts;
using Todo.Web.Api;

namespace Todo.Web.Auth;

/// <summary>
/// Development-only sign-in (Auth:Mode=Dev): every request is the fixed development user.
/// Registered only after the startup guard has confirmed the host environment is Development.
/// </summary>
public sealed class DevWebAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Dev";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(WebClaims.ObjectId, DevIdentity.DefaultUserId),
                new Claim(WebClaims.Name, DevIdentity.DefaultUserName),
                new Claim(WebClaims.SignInName, DevIdentity.DefaultSignInName),
            ],
            SchemeName,
            WebClaims.Name,
            roleType: null);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

/// <summary>Dev mode: identifies the signed-in development user to the local API with the X-Dev-User-Id header.</summary>
public sealed class DevTodoApiRequestAuthorizer(AuthenticationStateProvider authenticationStateProvider) : ITodoApiRequestAuthorizer
{
    public async Task AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var userId = state.User.FindFirst(WebClaims.ObjectId)?.Value ?? throw new ReauthenticationRequiredException();
        request.Headers.Remove(DevIdentity.UserIdHeader);
        request.Headers.Add(DevIdentity.UserIdHeader, userId);
    }
}
