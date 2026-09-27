using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Todo.Contracts;

namespace Todo.IntegrationTests;

[Trait("Target", "Local")]
[Trait("Target", "Azure")]
public sealed class AuthenticationTests : IDisposable
{
    private readonly HttpClient _anonymous = ApiTarget.CreateAnonymousClient();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public void Dispose() => _anonymous.Dispose();

    [AnonymousRejectionFact]
    public async Task Requests_without_a_token_return_401()
    {
        var id = Guid.NewGuid();
        var requests = new Func<Task<HttpResponseMessage>>[]
        {
            () => _anonymous.GetAsync("todos", _ct),
            () => _anonymous.GetAsync($"todos/{id}", _ct),
            () => _anonymous.PostAsJsonAsync("todos", new TodoRequest { Title = "t" }, _ct),
            () => _anonymous.PutAsJsonAsync($"todos/{id}", new TodoRequest { Title = "t" }, _ct),
            () => _anonymous.DeleteAsync($"todos/{id}", _ct),
        };

        foreach (var send in requests)
        {
            using var response = await send();
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [AnonymousRejectionFact]
    public async Task Invalid_bearer_token_returns_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "todos");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.valid-token");

        using var response = await _anonymous.SendAsync(request, _ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [AnonymousRejectionFact]
    public async Task Dev_identity_header_is_not_accepted()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "todos");
        request.Headers.Add(DevIdentity.UserIdHeader, Guid.NewGuid().ToString());

        using var response = await _anonymous.SendAsync(request, _ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
