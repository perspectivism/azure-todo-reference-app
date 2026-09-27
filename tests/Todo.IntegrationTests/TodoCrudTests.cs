using System.Net;
using System.Net.Http.Json;
using Todo.Contracts;

namespace Todo.IntegrationTests;

[Trait("Target", "Local")]
[Trait("Target", "Azure")]
public sealed class TodoCrudTests : IDisposable
{
    private readonly HttpClient _client = ApiTarget.CreateClientForUserA();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public void Dispose() => _client.Dispose();

    [UserFact]
    public async Task Create_retrieve_update_complete_and_delete()
    {
        // Create
        using var createResponse = await _client.PostAsJsonAsync("todos", new TodoRequest { Title = "  Integration todo  ", Description = "created by tests" }, _ct);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TodoResponse>(_ct);
        Assert.NotNull(created);
        Assert.Equal("Integration todo", created.Title);
        Assert.False(created.IsComplete);
        Assert.Equal($"/todos/{created.Id}", createResponse.Headers.Location?.OriginalString);

        try
        {
            // Retrieve
            var fetched = await _client.GetFromJsonAsync<TodoResponse>($"todos/{created.Id}", _ct);
            Assert.Equal(created, fetched);

            // Update (full replacement)
            using var updateResponse = await _client.PutAsJsonAsync($"todos/{created.Id}", new TodoRequest { Title = "Renamed", Description = null }, _ct);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            var updated = await updateResponse.Content.ReadFromJsonAsync<TodoResponse>(_ct);
            Assert.Equal("Renamed", updated!.Title);
            Assert.Null(updated.Description);
            Assert.Equal(created.CreatedUtc, updated.CreatedUtc);
            Assert.True(updated.UpdatedUtc >= created.UpdatedUtc);

            // Complete, then uncomplete
            using var completeResponse = await _client.PutAsJsonAsync($"todos/{created.Id}", new TodoRequest { Title = "Renamed", IsComplete = true }, _ct);
            Assert.True((await completeResponse.Content.ReadFromJsonAsync<TodoResponse>(_ct))!.IsComplete);
            using var reopenResponse = await _client.PutAsJsonAsync($"todos/{created.Id}", new TodoRequest { Title = "Renamed", IsComplete = false }, _ct);
            Assert.False((await reopenResponse.Content.ReadFromJsonAsync<TodoResponse>(_ct))!.IsComplete);
            Assert.False((await _client.GetFromJsonAsync<TodoResponse>($"todos/{created.Id}", _ct))!.IsComplete);
        }
        finally
        {
            // Delete
            using var deleteResponse = await _client.DeleteAsync($"todos/{created.Id}", _ct);
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        }

        using var afterDelete = await _client.GetAsync($"todos/{created.Id}", _ct);
        await ApiAssert.ProblemAsync(afterDelete, HttpStatusCode.NotFound);
    }

    [UserFact]
    public async Task List_pages_through_all_items_newest_first()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add((await ApiAssert.CreateAsync(_client, new TodoRequest { Title = $"Paging {i}" })).Id);
        }

        try
        {
            var seen = new List<TodoResponse>();
            string? token = null;
            var pages = 0;
            do
            {
                var url = token is null ? "todos?limit=2" : $"todos?limit=2&continuationToken={Uri.EscapeDataString(token)}";
                var page = await _client.GetFromJsonAsync<TodoListResponse>(url, _ct);
                Assert.NotNull(page);
                Assert.InRange(page.Items.Count, 0, 2);
                seen.AddRange(page.Items);
                token = page.ContinuationToken;
                pages++;
            }
            while (token is not null && pages < 1000);

            Assert.True(pages >= 2, "Three items with limit=2 must span at least two pages.");
            Assert.Equal(seen.Count, seen.Select(t => t.Id).Distinct().Count());
            var ours = seen.Where(t => ids.Contains(t.Id)).Select(t => t.Id).ToList();
            Assert.Equal(Enumerable.Reverse(ids), ours);
        }
        finally
        {
            foreach (var id in ids)
            {
                using var _ = await _client.DeleteAsync($"todos/{id}", _ct);
            }
        }
    }
}
