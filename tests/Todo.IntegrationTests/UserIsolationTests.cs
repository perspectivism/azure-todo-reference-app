using System.Net;
using System.Net.Http.Json;
using System.Text;
using Todo.Contracts;

namespace Todo.IntegrationTests;

[Trait("Target", "Local")]
[Trait("Target", "Azure")]
public sealed class UserIsolationTests : IDisposable
{
    private readonly HttpClient _userA = ApiTarget.CreateClientForUserA();
    private readonly HttpClient _userB = ApiTarget.CreateClientForUserB();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _userA.Dispose();
        _userB.Dispose();
    }

    [TwoUserFact]
    public async Task Another_users_todo_returns_404_for_read_update_and_delete()
    {
        var owned = await ApiAssert.CreateAsync(_userA, new TodoRequest { Title = "Private to user A" });
        try
        {
            using var read = await _userB.GetAsync($"todos/{owned.Id}", _ct);
            await ApiAssert.ProblemAsync(read, HttpStatusCode.NotFound);

            using var update = await _userB.PutAsJsonAsync($"todos/{owned.Id}", new TodoRequest { Title = "Changed by B", IsComplete = true }, _ct);
            await ApiAssert.ProblemAsync(update, HttpStatusCode.NotFound);

            using var delete = await _userB.DeleteAsync($"todos/{owned.Id}", _ct);
            await ApiAssert.ProblemAsync(delete, HttpStatusCode.NotFound);

            var listB = await _userB.GetFromJsonAsync<TodoListResponse>("todos?limit=100", _ct);
            Assert.DoesNotContain(listB!.Items, t => t.Id == owned.Id);

            var stillOwned = await _userA.GetFromJsonAsync<TodoResponse>($"todos/{owned.Id}", _ct);
            Assert.Equal(owned, stillOwned);
        }
        finally
        {
            using var _ = await _userA.DeleteAsync($"todos/{owned.Id}", _ct);
        }
    }

    [TwoUserFact]
    public async Task UserId_supplied_in_body_or_query_is_ignored()
    {
        // User B tries to create an item "for" user A by sending a userId; ownership still comes from B's identity.
        var body = """{"title":"Spoof attempt","userId":"00000000-0000-0000-0000-00000000aaaa"}""";
        using var createResponse = await _userB.PostAsync("todos?userId=00000000-0000-0000-0000-00000000aaaa", new StringContent(body, Encoding.UTF8, "application/json"), _ct);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<TodoResponse>(_ct))!;
        Assert.DoesNotContain("userId", await createResponse.Content.ReadAsStringAsync(_ct), StringComparison.OrdinalIgnoreCase);

        try
        {
            using var readByA = await _userA.GetAsync($"todos/{created.Id}", _ct);
            await ApiAssert.ProblemAsync(readByA, HttpStatusCode.NotFound);

            using var readByB = await _userB.GetAsync($"todos/{created.Id}", _ct);
            Assert.Equal(HttpStatusCode.OK, readByB.StatusCode);
        }
        finally
        {
            using var _ = await _userB.DeleteAsync($"todos/{created.Id}", _ct);
        }
    }
}
