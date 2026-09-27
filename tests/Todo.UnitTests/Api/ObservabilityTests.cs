using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Todo.Api.Auth;
using Todo.Api.Functions;
using Todo.Api.Todos;
using Todo.Contracts;
using Todo.UnitTests.TestSupport;

namespace Todo.UnitTests.Api;

public sealed class ObservabilityTests
{
    private const string UserId = "aaaaaaaa-0000-0000-0000-000000000001";
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Service_logs_are_structured_and_never_contain_todo_content()
    {
        var provider = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(provider).SetMinimumLevel(LogLevel.Trace));
        var service = new TodoService(new InMemoryTodoRepository(), TimeProvider.System, loggerFactory.CreateLogger<TodoService>());

        var created = await service.CreateAsync(UserId, new TodoRequest { Title = "SECRET-TITLE-7f3a", Description = "SECRET-DESCRIPTION-91c2" }, _ct);
        await service.UpdateAsync(UserId, created.Id.ToString(), new TodoRequest { Title = "SECRET-TITLE-UPDATED", Description = "SECRET-DESCRIPTION-UPDATED", IsComplete = true }, _ct);
        await service.DeleteAsync(UserId, created.Id.ToString(), _ct);

        Assert.Equal(3, provider.Entries.Count);
        Assert.DoesNotContain("SECRET-", provider.AllText, StringComparison.Ordinal);
        Assert.All(provider.Entries, e =>
        {
            Assert.Equal(created.Id.ToString(), e.Values["TodoId"]);
            Assert.Equal(UserId, e.Values["UserId"]);
        });
    }

    [Fact]
    public async Task Bearer_tokens_never_appear_in_logs()
    {
        const string tenantId = "72f988bf-0000-0000-0000-000000000001";
        const string clientId = "6a1c0b2e-0000-0000-0000-000000000002";
        using var rsa = RSA.Create(2048);
        var trustedKey = new RsaSecurityKey(rsa) { KeyId = "k1" };
        var configuration = new OpenIdConnectConfiguration { Issuer = $"https://login.microsoftonline.com/{tenantId}/v2.0" };
        configuration.SigningKeys.Add(trustedKey);
        var provider = new CapturingLoggerProvider();

        using var services = TestHost.BuildAuthServices(
            TestHost.Configuration(("Auth:Mode", "Entra"), ("Auth:TenantId", tenantId), ("Auth:ClientId", clientId)),
            "Production",
            s =>
            {
                s.AddLogging(b => b.AddProvider(provider).SetMinimumLevel(LogLevel.Trace));
                s.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o => o.Configuration = configuration);
            });

        string CreateToken(SecurityKey key, string audience) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = configuration.Issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(30),
            Claims = new Dictionary<string, object> { ["tid"] = tenantId, ["scp"] = "access_as_user", ["oid"] = UserId },
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        });

        var tokens = new[]
        {
            CreateToken(trustedKey, clientId),
            CreateToken(trustedKey, "api://wrong-audience"),
            CreateToken(new RsaSecurityKey(RSA.Create(2048)) { KeyId = "k1" }, clientId),
        };

        foreach (var token in tokens)
        {
            await CurrentUser.AuthenticateAsync(TestHost.HttpContext(services, ("Authorization", $"Bearer {token}")));
        }

        Assert.NotEmpty(provider.Entries);
        foreach (var token in tokens)
        {
            // Neither the whole token nor its signature segment is logged.
            Assert.DoesNotContain(token, provider.AllText, StringComparison.Ordinal);
            Assert.DoesNotContain(token.Split('.')[2], provider.AllText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Fault_endpoint_returns_404_by_default()
    {
        var currentUser = new CurrentUser();
        currentUser.Set(UserId);
        var functions = new DiagnosticsFunctions(new DiagnosticsOptions(), currentUser, NullLogger<DiagnosticsFunctions>.Instance);

        var result = functions.TriggerFault(new DefaultHttpContext().Request);

        Assert.Equal(404, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public void Fault_endpoint_throws_when_enabled()
    {
        var currentUser = new CurrentUser();
        currentUser.Set(UserId);
        var functions = new DiagnosticsFunctions(new DiagnosticsOptions { EnableFaultInjection = true }, currentUser, NullLogger<DiagnosticsFunctions>.Instance);

        var ex = Assert.Throws<InvalidOperationException>(() => functions.TriggerFault(new DefaultHttpContext().Request));

        Assert.Equal(DiagnosticsFunctions.FaultMessage, ex.Message);
    }

    [Fact]
    public void Fault_injection_is_disabled_unless_configured()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTodoApiForTest();

        using var provider = services.BuildServiceProvider();

        Assert.False(provider.GetRequiredService<DiagnosticsOptions>().EnableFaultInjection);
    }
}

internal static class TodoApiTestRegistration
{
    public static IServiceCollection AddTodoApiForTest(this IServiceCollection services)
        => Todo.Api.TodoApiServiceCollectionExtensions.AddTodoApi(
            services,
            TestHost.Configuration(("Auth:Mode", "Dev"), ("Storage:Provider", "InMemory")),
            new TestHostEnvironment("Development"));
}
