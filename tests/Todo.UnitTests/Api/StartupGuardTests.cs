using Microsoft.Extensions.DependencyInjection;
using Todo.Api;
using Todo.UnitTests.TestSupport;

namespace Todo.UnitTests.Api;

public sealed class StartupGuardTests
{
    private static readonly (string, string?)[] ValidEntra =
    [
        ("Auth:Mode", "Entra"),
        ("Auth:TenantId", "11111111-1111-1111-1111-111111111111"),
        ("Auth:ClientId", "22222222-2222-2222-2222-222222222222"),
    ];

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    public void Dev_auth_mode_throws_outside_Development(string environment)
    {
        var configuration = TestHost.Configuration(("Auth:Mode", "Dev"));

        var ex = Assert.Throws<InvalidOperationException>(() => TestHost.BuildAuthServices(configuration, environment));

        Assert.Contains("Development", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dev_auth_mode_is_allowed_in_Development()
    {
        using var services = TestHost.BuildAuthServices(TestHost.Configuration(("Auth:Mode", "Dev")), "Development");

        Assert.NotNull(services);
    }

    [Fact]
    public void Entra_is_the_default_mode_and_requires_tenant_and_client_ids()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => TestHost.BuildAuthServices(TestHost.Configuration(), "Production"));

        Assert.Contains("Auth:TenantId", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Entra_mode_starts_in_Production_with_required_settings()
    {
        using var services = TestHost.BuildAuthServices(TestHost.Configuration(ValidEntra), "Production");

        Assert.NotNull(services);
    }

    [Fact]
    public void InMemory_storage_throws_outside_Development()
    {
        var services = new ServiceCollection();
        var configuration = TestHost.Configuration(("Storage:Provider", "InMemory"));

        Assert.Throws<InvalidOperationException>(() =>
            services.AddTodoStorage(configuration, new TestHostEnvironment("Production")));
    }
}
