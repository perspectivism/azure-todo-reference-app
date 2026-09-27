using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Client;
using Todo.UnitTests.TestSupport;
using Todo.Web;
using Todo.Web.Api;
using Todo.Web.Auth;

namespace Todo.UnitTests.Web;

public sealed class EntraWebAuthenticationTests
{
    private const string Scope = "api://6a1c0b2e-0000-0000-0000-000000000002/access_as_user";

    private sealed class FixedAuthenticationStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }

    private sealed class FakeHeaderProvider(Func<IEnumerable<string>, ClaimsPrincipal?, string> create) : IAuthorizationHeaderProvider
    {
        public Task<string> CreateAuthorizationHeaderForUserAsync(IEnumerable<string> scopes, AuthorizationHeaderProviderOptions? authorizationHeaderProviderOptions = null, ClaimsPrincipal? claimsPrincipal = null, CancellationToken cancellationToken = default)
            => Task.FromResult(create(scopes, claimsPrincipal));

        public Task<string> CreateAuthorizationHeaderForAppAsync(string scopes, AuthorizationHeaderProviderOptions? downstreamApiOptions = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The web app only calls the API on behalf of the signed-in user.");

        public Task<string> CreateAuthorizationHeaderAsync(IEnumerable<string> scopes, AuthorizationHeaderProviderOptions? options = null, ClaimsPrincipal? claimsPrincipal = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static readonly ClaimsPrincipal SignedIn = new(new ClaimsIdentity([new Claim("oid", Guid.NewGuid().ToString())], "oidc"));

    [Fact]
    public async Task Attaches_a_delegated_bearer_token_for_the_api_scope_of_the_signed_in_user()
    {
        IEnumerable<string>? requestedScopes = null;
        ClaimsPrincipal? requestedFor = null;
        var authorizer = new EntraTodoApiRequestAuthorizer(
            new FixedAuthenticationStateProvider(SignedIn),
            new FakeHeaderProvider((scopes, user) =>
            {
                requestedScopes = scopes;
                requestedFor = user;
                return "Bearer header.payload.signature";
            }),
            new TodoApiOptions { Scope = Scope });
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://api.test/todos");

        await authorizer.AuthorizeAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal([Scope], requestedScopes);
        Assert.Same(SignedIn, requestedFor);
        Assert.False(request.Headers.Contains(Todo.Contracts.DevIdentity.UserIdHeader));
    }

    [Fact]
    public async Task Missing_cached_account_requires_sign_in_again()
    {
        var authorizer = new EntraTodoApiRequestAuthorizer(
            new FixedAuthenticationStateProvider(SignedIn),
            new FakeHeaderProvider((_, _) => throw new MsalUiRequiredException("user_null", "No account in the token cache.")),
            new TodoApiOptions { Scope = Scope });
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://api.test/todos");

        await Assert.ThrowsAsync<ReauthenticationRequiredException>(() => authorizer.AuthorizeAsync(request, TestContext.Current.CancellationToken));
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task Anonymous_user_requires_sign_in()
    {
        var authorizer = new EntraTodoApiRequestAuthorizer(
            new FixedAuthenticationStateProvider(new ClaimsPrincipal(new ClaimsIdentity())),
            new FakeHeaderProvider((_, _) => throw new InvalidOperationException("must not be called")),
            new TodoApiOptions { Scope = Scope });
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://api.test/todos");

        await Assert.ThrowsAsync<ReauthenticationRequiredException>(() => authorizer.AuthorizeAsync(request, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null, "22222222-2222-2222-2222-222222222222", Scope)]
    [InlineData("11111111-1111-1111-1111-111111111111", null, Scope)]
    [InlineData("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", null)]
    public void Entra_mode_requires_tenant_client_and_scope(string? tenantId, string? clientId, string? scope)
    {
        var azureAd = TestHost.Configuration(("TenantId", tenantId), ("ClientId", clientId));

        Assert.Throws<InvalidOperationException>(() => TodoWebServiceCollectionExtensions.ValidateStartup(
            new WebAuthOptions { Mode = WebAuthMode.Entra },
            new TodoApiOptions { BaseUrl = "https://apim.example/", Scope = scope },
            new TestHostEnvironment("Production"),
            azureAd));
    }

    [Fact]
    public void Entra_mode_with_complete_settings_is_valid_in_Production()
    {
        var azureAd = TestHost.Configuration(("TenantId", "11111111-1111-1111-1111-111111111111"), ("ClientId", "22222222-2222-2222-2222-222222222222"));

        TodoWebServiceCollectionExtensions.ValidateStartup(
            new WebAuthOptions { Mode = WebAuthMode.Entra },
            new TodoApiOptions { BaseUrl = "https://apim.example/", Scope = Scope },
            new TestHostEnvironment("Production"),
            azureAd);
    }
}
