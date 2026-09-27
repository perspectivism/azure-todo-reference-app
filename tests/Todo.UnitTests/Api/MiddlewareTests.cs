using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Todo.Api.Auth;
using Todo.Api.Functions;
using Todo.Api.Http;
using Todo.Api.Todos;
using Todo.Contracts;
using Todo.UnitTests.TestSupport;

namespace Todo.UnitTests.Api;

public sealed class AuthenticationMiddlewareTests : IDisposable
{
    private const string TenantId = "72f988bf-0000-0000-0000-000000000001";
    private const string ClientId = "6a1c0b2e-0000-0000-0000-000000000002";

    private readonly CapturingLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly AuthenticationMiddleware _middleware;
    private readonly RSA _rsa = RSA.Create(2048);

    public AuthenticationMiddlewareTests()
    {
        _loggerFactory = LoggerFactory.Create(b => b.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
        _middleware = new AuthenticationMiddleware(_loggerFactory.CreateLogger<AuthenticationMiddleware>());
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
        _rsa.Dispose();
    }

    /// <summary>Real Entra-mode registration with a static OpenID configuration, so no network is used.</summary>
    private ServiceProvider EntraServices()
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = $"https://login.microsoftonline.com/{TenantId}/v2.0" };
        configuration.SigningKeys.Add(new RsaSecurityKey(_rsa) { KeyId = "k1" });
        return TestHost.BuildAuthServices(
            TestHost.Configuration(("Auth:Mode", "Entra"), ("Auth:TenantId", TenantId), ("Auth:ClientId", ClientId)),
            "Production",
            s => s.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o => o.Configuration = configuration));
    }

    [Fact]
    public async Task Health_is_anonymous()
    {
        using var services = EntraServices();
        var currentUser = new CurrentUser();

        var result = await _middleware.AuthenticateAsync(HealthFunction.FunctionName, TestHost.HttpContext(services), currentUser);

        Assert.Null(result);
        Assert.Throws<InvalidOperationException>(() => currentUser.UserId);
    }

    [Theory]
    [InlineData("ListTodos")]
    [InlineData("GetTodo")]
    [InlineData("CreateTodo")]
    [InlineData("UpdateTodo")]
    [InlineData("DeleteTodo")]
    [InlineData("TriggerFault")]
    public async Task Protected_functions_without_a_token_get_401_problem_and_challenge(string functionName)
    {
        using var services = EntraServices();
        var httpContext = TestHost.HttpContext(services);
        var currentUser = new CurrentUser();

        var result = await _middleware.AuthenticateAsync(functionName, httpContext, currentUser);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(401, problem.StatusCode);
        Assert.IsType<ProblemDetails>(problem.Value);
        Assert.Equal("Bearer", httpContext.Response.Headers.WWWAuthenticate.ToString());
        Assert.Throws<InvalidOperationException>(() => currentUser.UserId);
    }

    [Fact]
    public async Task Rejected_token_is_logged_without_the_token()
    {
        using var services = EntraServices();

        // A syntactically valid but untrusted JWT, built at runtime so no token-shaped literal exists in the repository.
        static string Segment(string json) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = string.Join('.', Segment("""{"alg":"RS256","kid":"k1"}"""), Segment("""{"sub":"test-subject"}"""), "c2lnbmF0dXJlLXNlY3JldA");

        var result = await _middleware.AuthenticateAsync("ListTodos", TestHost.HttpContext(services, ("Authorization", $"Bearer {token}")), new CurrentUser());

        Assert.Equal(401, Assert.IsType<ObjectResult>(result).StatusCode);
        var rejection = Assert.Single(_logs.Entries, e => e.Category == typeof(AuthenticationMiddleware).FullName);
        Assert.Equal(LogLevel.Warning, rejection.Level);
        Assert.Equal("ListTodos", rejection.Values["FunctionName"]);
        Assert.Equal("True", rejection.Values["HasAuthorizationHeader"]);
        Assert.DoesNotContain(token, _logs.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain("c2lnbmF0dXJlLXNlY3JldA", _logs.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authenticated_caller_becomes_the_current_user()
    {
        using var services = TestHost.BuildAuthServices(TestHost.Configuration(("Auth:Mode", "Dev")));
        var userId = Guid.NewGuid().ToString();
        var currentUser = new CurrentUser();

        var result = await _middleware.AuthenticateAsync("ListTodos", TestHost.HttpContext(services, (DevIdentity.UserIdHeader, userId)), currentUser);

        Assert.Null(result);
        Assert.Equal(userId, currentUser.UserId);
    }
}

public sealed class ProblemHandlingMiddlewareTests : IDisposable
{
    private readonly CapturingLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly ProblemHandlingMiddleware _middleware;

    public ProblemHandlingMiddlewareTests()
    {
        _loggerFactory = LoggerFactory.Create(b => b.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
        _middleware = new ProblemHandlingMiddleware(_loggerFactory.CreateLogger<ProblemHandlingMiddleware>());
    }

    public void Dispose() => _loggerFactory.Dispose();

    [Fact]
    public void Validation_exception_becomes_400_with_field_errors()
    {
        var errors = new Dictionary<string, string[]> { ["title"] = ["Title is required."] };

        var result = Assert.IsType<ObjectResult>(_middleware.ToProblem(new TodoValidationException(errors), "CreateTodo"));

        Assert.Equal(400, result.StatusCode);
        var problem = Assert.IsType<ValidationProblemDetails>(result.Value);
        Assert.Equal(["Title is required."], problem.Errors["title"]);
        Assert.Empty(_logs.Entries);
    }

    [Fact]
    public void Wrapped_validation_exception_is_still_400()
    {
        var inner = new TodoValidationException(new Dictionary<string, string[]> { ["title"] = ["x"] });

        var result = Assert.IsType<ObjectResult>(_middleware.ToProblem(new InvalidOperationException("wrapper", inner), "CreateTodo"));

        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public void Invalid_continuation_token_becomes_400()
    {
        var result = Assert.IsType<ObjectResult>(_middleware.ToProblem(new InvalidContinuationTokenException(), "ListTodos"));

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("continuationToken", Assert.IsType<ValidationProblemDetails>(result.Value).Errors.Keys);
    }

    [Fact]
    public void Unexpected_exception_becomes_generic_500_and_is_logged()
    {
        var failure = new InvalidOperationException("Cosmos request failed: internal-host:8081 secret-detail");

        var result = Assert.IsType<ObjectResult>(_middleware.ToProblem(failure, "GetTodo"));

        Assert.Equal(500, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.DoesNotContain("secret-detail", problem.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("internal-host", problem.Title, StringComparison.Ordinal);
        var logged = Assert.Single(_logs.Entries);
        Assert.Equal(LogLevel.Error, logged.Level);
        Assert.Equal("GetTodo", logged.Values["FunctionName"]);
        Assert.Contains("secret-detail", logged.Exception, StringComparison.Ordinal);
    }
}
