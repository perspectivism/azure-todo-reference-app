using System.Net;
using System.Net.Http.Json;
using Todo.Contracts;

namespace Todo.IntegrationTests;

[Trait("Target", "Local")]
[Trait("Target", "Azure")]
public sealed class HealthTests
{
    [ApiFact]
    public async Task Health_returns_200_without_authentication()
    {
        using var client = ApiTarget.CreateAnonymousClient();

        using var response = await client.GetAsync("health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(HealthResponse.Healthy, body?.Status);
    }
}
