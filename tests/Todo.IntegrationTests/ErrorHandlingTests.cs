using System.Net;
using System.Net.Http.Json;
using System.Text;
using Todo.Contracts;

namespace Todo.IntegrationTests;

[Trait("Target", "Local")]
[Trait("Target", "Azure")]
public sealed class ErrorHandlingTests : IDisposable
{
    private readonly HttpClient _client = ApiTarget.CreateClientForUserA();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public void Dispose() => _client.Dispose();

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    [UserFact]
    public async Task Malformed_json_returns_400()
    {
        using var response = await _client.PostAsync("todos", Json("{\"title\": "), _ct);

        await ApiAssert.ProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [UserFact]
    public async Task Invalid_title_returns_400_with_field_error()
    {
        using var empty = await _client.PostAsJsonAsync("todos", new TodoRequest { Title = "   " }, _ct);
        await ApiAssert.ProblemAsync(empty, HttpStatusCode.BadRequest);
        Assert.Contains("\"title\"", await empty.Content.ReadAsStringAsync(_ct), StringComparison.Ordinal);

        using var tooLong = await _client.PostAsJsonAsync("todos", new TodoRequest { Title = new string('x', TodoLimits.TitleMaxLength + 1) }, _ct);
        await ApiAssert.ProblemAsync(tooLong, HttpStatusCode.BadRequest);
    }

    [UserFact]
    public async Task Invalid_paging_parameters_return_400()
    {
        using var badLimit = await _client.GetAsync("todos?limit=101", _ct);
        await ApiAssert.ProblemAsync(badLimit, HttpStatusCode.BadRequest);

        using var badToken = await _client.GetAsync("todos?continuationToken=not-a-token", _ct);
        await ApiAssert.ProblemAsync(badToken, HttpStatusCode.BadRequest);
    }

    [UserFact]
    public async Task Body_over_16KB_returns_413()
    {
        var body = $$"""{"title":"t","description":"{{new string('d', TodoLimits.MaxRequestBodyBytes)}}"}""";

        using var response = await _client.PostAsync("todos", Json(body), _ct);

        await ApiAssert.ProblemAsync(response, HttpStatusCode.RequestEntityTooLarge);
    }

    [UserFact]
    public async Task Fault_injection_endpoint_returns_404_by_default()
    {
        using var response = await _client.PostAsync("diagnostics/fault", content: null, _ct);

        await ApiAssert.ProblemAsync(response, HttpStatusCode.NotFound);
    }

    [UserFact]
    public async Task Unknown_and_malformed_ids_return_404()
    {
        foreach (var id in new[] { Guid.NewGuid().ToString(), "not-a-guid" })
        {
            using var get = await _client.GetAsync($"todos/{id}", _ct);
            await ApiAssert.ProblemAsync(get, HttpStatusCode.NotFound);

            using var put = await _client.PutAsJsonAsync($"todos/{id}", new TodoRequest { Title = "t" }, _ct);
            await ApiAssert.ProblemAsync(put, HttpStatusCode.NotFound);

            using var delete = await _client.DeleteAsync($"todos/{id}", _ct);
            await ApiAssert.ProblemAsync(delete, HttpStatusCode.NotFound);
        }
    }
}
