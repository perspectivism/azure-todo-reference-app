using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Todo.Api.Auth;
using Todo.UnitTests.TestSupport;

namespace Todo.UnitTests.Api;

/// <summary>
/// Exercises the API's real JwtBearer registration with tokens signed by a locally generated key.
/// The OpenID configuration is supplied statically, so no network access is needed.
/// </summary>
public sealed class JwtValidationTests : IDisposable
{
    private const string TenantId = "72f988bf-0000-0000-0000-000000000001";
    private const string ClientId = "6a1c0b2e-0000-0000-0000-000000000002";
    private const string ObjectId = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";
    private static readonly string Issuer = $"https://login.microsoftonline.com/{TenantId}/v2.0";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _signingKey;
    private readonly ServiceProvider _services;

    public JwtValidationTests()
    {
        _signingKey = new RsaSecurityKey(_rsa) { KeyId = "test-key" };
        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        configuration.SigningKeys.Add(_signingKey);

        _services = TestHost.BuildAuthServices(
            TestHost.Configuration(("Auth:Mode", "Entra"), ("Auth:TenantId", TenantId), ("Auth:ClientId", ClientId)),
            "Production",
            services => services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o => o.Configuration = configuration));
    }

    public void Dispose()
    {
        _services.Dispose();
        _rsa.Dispose();
    }

    public enum Defect
    {
        None,
        WrongIssuer,
        WrongAudience,
        WrongTenant,
        MissingScope,
        WrongScope,
        Expired,
        NotYetValid,
        MissingObjectId,
        UntrustedSigningKey,
        Unsigned,
    }

    private string CreateToken(Defect defect = Defect.None, string audience = ClientId, string scope = "access_as_user")
    {
        var now = DateTime.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["tid"] = defect == Defect.WrongTenant ? "00000000-0000-0000-0000-00000000dead" : TenantId,
            ["oid"] = ObjectId,
            ["name"] = "Test User",
        };
        if (defect != Defect.MissingScope)
        {
            claims["scp"] = defect == Defect.WrongScope ? "User.Read" : scope;
        }

        if (defect == Defect.MissingObjectId)
        {
            claims.Remove("oid");
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = defect == Defect.WrongIssuer ? "https://login.microsoftonline.com/00000000-0000-0000-0000-00000000beef/v2.0" : Issuer,
            Audience = defect == Defect.WrongAudience ? "api://some-other-api" : audience,
            IssuedAt = now.AddMinutes(-10),
            NotBefore = defect == Defect.NotYetValid ? now.AddMinutes(30) : now.AddMinutes(-10),
            Expires = defect switch
            {
                Defect.Expired => now.AddMinutes(-5),
                Defect.NotYetValid => now.AddMinutes(60),
                _ => now.AddMinutes(50),
            },
            Claims = claims,
        };

        if (defect == Defect.UntrustedSigningKey)
        {
            descriptor.SigningCredentials = new SigningCredentials(new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" }, SecurityAlgorithms.RsaSha256);
        }
        else if (defect != Defect.Unsigned)
        {
            descriptor.SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256);
        }

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private Task<string?> AuthenticateAsync(string? token)
    {
        var context = token is null
            ? TestHost.HttpContext(_services)
            : TestHost.HttpContext(_services, ("Authorization", $"Bearer {token}"));
        return CurrentUser.AuthenticateAsync(context);
    }

    [Fact]
    public async Task Valid_token_yields_the_oid_as_the_user_id()
    {
        Assert.Equal(ObjectId, await AuthenticateAsync(CreateToken()));
    }

    [Fact]
    public async Task Application_id_uri_audience_is_accepted()
    {
        Assert.Equal(ObjectId, await AuthenticateAsync(CreateToken(audience: $"api://{ClientId}")));
    }

    [Fact]
    public async Task Scope_is_found_among_multiple_scopes()
    {
        Assert.Equal(ObjectId, await AuthenticateAsync(CreateToken(scope: "User.Read access_as_user")));
    }

    [Theory]
    [InlineData(Defect.WrongIssuer)]
    [InlineData(Defect.WrongAudience)]
    [InlineData(Defect.WrongTenant)]
    [InlineData(Defect.MissingScope)]
    [InlineData(Defect.WrongScope)]
    [InlineData(Defect.Expired)]
    [InlineData(Defect.NotYetValid)]
    [InlineData(Defect.MissingObjectId)]
    [InlineData(Defect.UntrustedSigningKey)]
    [InlineData(Defect.Unsigned)]
    public async Task Invalid_tokens_are_rejected(Defect defect)
    {
        Assert.Null(await AuthenticateAsync(CreateToken(defect)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-jwt")]
    public async Task Requests_without_a_valid_token_are_rejected_in_Entra_mode(string? token)
    {
        Assert.Null(await AuthenticateAsync(token));
    }

    [Fact]
    public void Claim_rules_report_the_failing_rule()
    {
        var options = new AuthOptions { TenantId = TenantId, ClientId = ClientId };
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("tid", TenantId), new System.Security.Claims.Claim("scp", "access_as_user")], "test"));

        Assert.Contains("oid", EntraTokenValidation.GetClaimFailure(principal, options), StringComparison.Ordinal);
    }
}
