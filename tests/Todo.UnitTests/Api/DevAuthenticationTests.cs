using Todo.Api.Auth;
using Todo.Contracts;
using Todo.UnitTests.TestSupport;

namespace Todo.UnitTests.Api;

public sealed class DevAuthenticationTests
{
    private static readonly Microsoft.Extensions.Configuration.IConfiguration DevConfig = TestHost.Configuration(("Auth:Mode", "Dev"));

    [Fact]
    public async Task Header_guid_becomes_the_callers_object_id()
    {
        using var services = TestHost.BuildAuthServices(DevConfig);
        var userId = Guid.NewGuid().ToString();

        var result = await CurrentUser.AuthenticateAsync(TestHost.HttpContext(services, (DevIdentity.UserIdHeader, userId.ToUpperInvariant())));

        Assert.Equal(userId, result);
    }

    [Fact]
    public async Task Missing_header_falls_back_to_the_fixed_development_user()
    {
        using var services = TestHost.BuildAuthServices(DevConfig);

        var result = await CurrentUser.AuthenticateAsync(TestHost.HttpContext(services));

        Assert.Equal(DevIdentity.DefaultUserId, result);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public async Task Invalid_header_is_rejected(string value)
    {
        using var services = TestHost.BuildAuthServices(DevConfig);

        var result = await CurrentUser.AuthenticateAsync(TestHost.HttpContext(services, (DevIdentity.UserIdHeader, value)));

        Assert.Null(result);
    }

    [Fact]
    public async Task Dev_header_is_ignored_in_Entra_mode()
    {
        var entra = TestHost.Configuration(
            ("Auth:Mode", "Entra"),
            ("Auth:TenantId", "11111111-1111-1111-1111-111111111111"),
            ("Auth:ClientId", "22222222-2222-2222-2222-222222222222"));
        using var services = TestHost.BuildAuthServices(entra, "Production");

        var result = await CurrentUser.AuthenticateAsync(TestHost.HttpContext(services, (DevIdentity.UserIdHeader, Guid.NewGuid().ToString())));

        Assert.Null(result);
    }
}
