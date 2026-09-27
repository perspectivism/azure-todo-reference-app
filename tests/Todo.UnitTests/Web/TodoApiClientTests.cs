using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Components.Authorization;
using Todo.Contracts;
using Todo.UnitTests.TestSupport;
using Todo.Web;
using Todo.Web.Api;
using Todo.Web.Auth;

namespace Todo.UnitTests.Web;

public sealed class TodoApiClientTests
{
    private sealed class RecordingHandler(HttpStatusCode status, string body, string mediaType = "application/json") : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) });
        }
    }

    private sealed class FixedAuthenticationStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }

    private static TodoApiClient CreateClient(RecordingHandler handler, string userId = "0f8fad5b-d9cb-469f-a165-70867728950e")
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(WebClaims.ObjectId, userId)], "test"));
        var authorizer = new DevTodoApiRequestAuthorizer(new FixedAuthenticationStateProvider(user));
        return new TodoApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }, authorizer);
    }

    [Fact]
    public async Task Dev_mode_sends_the_signed_in_users_id_and_escapes_the_continuation_token()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"items":[],"continuationToken":null}""");

        await CreateClient(handler).ListAsync(20, "a+b/c=", TestContext.Current.CancellationToken);

        Assert.Equal("http://api.test/todos?limit=20&continuationToken=a%2Bb%2Fc%3D", handler.Request!.RequestUri!.AbsoluteUri);
        Assert.Equal("0f8fad5b-d9cb-469f-a165-70867728950e", Assert.Single(handler.Request.Headers.GetValues(DevIdentity.UserIdHeader)));
        Assert.Null(handler.Request.Headers.Authorization);
    }

    [Fact]
    public async Task Validation_problem_is_surfaced_as_field_errors()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"title":["Title is required."]}}""",
            "application/problem+json");

        var ex = await Assert.ThrowsAsync<TodoApiException>(() =>
            CreateClient(handler).CreateAsync(new TodoRequest { Title = "x" }, TestContext.Current.CancellationToken));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(["Title is required."], ex.Errors["title"]);
    }

    [Fact]
    public async Task Not_found_is_surfaced_with_its_status_code()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound, """{"status":404}""", "application/problem+json");

        var ex = await Assert.ThrowsAsync<TodoApiException>(() =>
            CreateClient(handler).DeleteAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));

        Assert.Equal(404, ex.StatusCode);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Web_dev_auth_mode_throws_outside_Development(string environment)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => TodoWebServiceCollectionExtensions.ValidateStartup(
            new WebAuthOptions { Mode = WebAuthMode.Dev },
            new TodoApiOptions { BaseUrl = "http://localhost:7071/" },
            new TestHostEnvironment(environment)));

        Assert.Contains("Development", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Web_requires_an_api_base_url()
    {
        Assert.Throws<InvalidOperationException>(() => TodoWebServiceCollectionExtensions.ValidateStartup(
            new WebAuthOptions { Mode = WebAuthMode.Dev },
            new TodoApiOptions(),
            new TestHostEnvironment("Development")));
    }

    [Theory]
    [InlineData("/todos", "/todos")]
    [InlineData(null, "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example/", "/")]
    [InlineData("/\\evil.example/", "/")]
    public void Return_urls_are_restricted_to_local_paths(string? returnUrl, string expected)
    {
        Assert.Equal(expected, AuthenticationEndpoints.SafeReturnUrl(returnUrl));
    }
}
