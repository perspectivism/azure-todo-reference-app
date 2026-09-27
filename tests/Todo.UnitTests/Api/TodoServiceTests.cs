using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Todo.Api.Todos;
using Todo.Contracts;

namespace Todo.UnitTests.Api;

public sealed class TodoServiceTests
{
    private const string UserA = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string UserB = "bbbbbbbb-0000-0000-0000-000000000002";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
    private readonly TodoService _service;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public TodoServiceTests()
    {
        _service = new TodoService(new InMemoryTodoRepository(), _time, NullLogger<TodoService>.Instance);
    }

    [Fact]
    public async Task Create_trims_title_and_sets_server_timestamps()
    {
        var created = await _service.CreateAsync(UserA, new TodoRequest { Title = "  Buy milk  ", Description = "2 litres" }, _ct);

        Assert.Equal("Buy milk", created.Title);
        Assert.Equal("2 litres", created.Description);
        Assert.False(created.IsComplete);
        Assert.Equal(_time.GetUtcNow(), created.CreatedUtc);
        Assert.Equal(created.CreatedUtc, created.UpdatedUtc);
        Assert.Equal(TimeSpan.Zero, created.CreatedUtc.Offset);
    }

    [Fact]
    public async Task Create_rejects_invalid_request()
    {
        var ex = await Assert.ThrowsAsync<TodoValidationException>(
            () => _service.CreateAsync(UserA, new TodoRequest { Title = " " }, _ct));

        Assert.Contains("title", ex.Errors.Keys);
    }

    [Fact]
    public async Task Update_replaces_fields_and_keeps_created_timestamp()
    {
        var created = await _service.CreateAsync(UserA, new TodoRequest { Title = "Draft", Description = "x" }, _ct);
        _time.Advance(TimeSpan.FromMinutes(5));

        var updated = await _service.UpdateAsync(UserA, created.Id.ToString(), new TodoRequest { Title = "Final", IsComplete = true }, _ct);

        Assert.NotNull(updated);
        Assert.Equal("Final", updated.Title);
        Assert.Null(updated.Description);
        Assert.True(updated.IsComplete);
        Assert.Equal(created.CreatedUtc, updated.CreatedUtc);
        Assert.Equal(created.CreatedUtc.AddMinutes(5), updated.UpdatedUtc);
    }

    [Fact]
    public async Task Complete_and_uncomplete_round_trip()
    {
        var created = await _service.CreateAsync(UserA, new TodoRequest { Title = "Task" }, _ct);
        var id = created.Id.ToString();

        var completed = await _service.UpdateAsync(UserA, id, new TodoRequest { Title = "Task", IsComplete = true }, _ct);
        var reopened = await _service.UpdateAsync(UserA, id, new TodoRequest { Title = "Task", IsComplete = false }, _ct);

        Assert.True(completed!.IsComplete);
        Assert.False(reopened!.IsComplete);
        Assert.False((await _service.GetAsync(UserA, id, _ct))!.IsComplete);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("{00000000-0000-0000-0000-000000000000}")]
    public async Task Malformed_ids_are_not_found(string id)
    {
        Assert.Null(await _service.GetAsync(UserA, id, _ct));
        Assert.Null(await _service.UpdateAsync(UserA, id, new TodoRequest { Title = "t" }, _ct));
        Assert.False(await _service.DeleteAsync(UserA, id, _ct));
    }

    [Fact]
    public async Task Unknown_id_is_not_found()
    {
        var id = Guid.NewGuid().ToString();

        Assert.Null(await _service.GetAsync(UserA, id, _ct));
        Assert.Null(await _service.UpdateAsync(UserA, id, new TodoRequest { Title = "t" }, _ct));
        Assert.False(await _service.DeleteAsync(UserA, id, _ct));
    }

    [Fact]
    public async Task Another_users_todo_is_indistinguishable_from_a_missing_one()
    {
        var owned = await _service.CreateAsync(UserA, new TodoRequest { Title = "A's todo" }, _ct);
        var id = owned.Id.ToString();

        Assert.Null(await _service.GetAsync(UserB, id, _ct));
        Assert.Null(await _service.UpdateAsync(UserB, id, new TodoRequest { Title = "hijacked", IsComplete = true }, _ct));
        Assert.False(await _service.DeleteAsync(UserB, id, _ct));
        Assert.Empty((await _service.ListAsync(UserB, 50, null, _ct)).Items);

        var stillOwned = await _service.GetAsync(UserA, id, _ct);
        Assert.Equal(owned, stillOwned);
    }

    [Fact]
    public async Task List_returns_only_own_items_newest_first_with_paging()
    {
        var created = new List<TodoResponse>();
        for (var i = 0; i < 5; i++)
        {
            created.Add(await _service.CreateAsync(UserA, new TodoRequest { Title = $"A{i}" }, _ct));
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        await _service.CreateAsync(UserB, new TodoRequest { Title = "B" }, _ct);

        var first = await _service.ListAsync(UserA, 2, null, _ct);
        var second = await _service.ListAsync(UserA, 2, first.ContinuationToken, _ct);
        var third = await _service.ListAsync(UserA, 2, second.ContinuationToken, _ct);

        Assert.NotNull(first.ContinuationToken);
        Assert.NotNull(second.ContinuationToken);
        Assert.Null(third.ContinuationToken);
        var all = first.Items.Concat(second.Items).Concat(third.Items).Select(t => t.Title).ToList();
        Assert.Equal(["A4", "A3", "A2", "A1", "A0"], all);
    }

    [Fact]
    public async Task List_rejects_invalid_continuation_token()
    {
        await Assert.ThrowsAsync<InvalidContinuationTokenException>(() => _service.ListAsync(UserA, 10, "garbage", _ct));
    }
}
