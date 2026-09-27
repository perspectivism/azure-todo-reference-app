using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Todo.Contracts;

namespace Todo.Api.Auth;

/// <summary>
/// Development-only authentication (Auth:Mode=Dev). The caller's object id comes from the X-Dev-User-Id header
/// (a GUID), or the fixed development user when the header is absent. A header that is not a GUID is rejected.
/// Registered only after <see cref="AuthStartupGuard"/> has confirmed the host environment is Development.
/// </summary>
public sealed class DevAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Dev";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = DevIdentity.DefaultUserId;
        if (Request.Headers.TryGetValue(DevIdentity.UserIdHeader, out var values))
        {
            if (values.Count != 1 || !Guid.TryParseExact(values[0], "D", out var parsed))
            {
                return Task.FromResult(AuthenticateResult.Fail($"{DevIdentity.UserIdHeader} must be a single GUID."));
            }

            userId = parsed.ToString("D");
        }

        var identity = new ClaimsIdentity(
            [new Claim(EntraClaims.ObjectId, userId), new Claim(EntraClaims.Name, DevIdentity.DefaultUserName)],
            SchemeName,
            EntraClaims.Name,
            roleType: null);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
