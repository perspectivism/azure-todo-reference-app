using Todo.Contracts;
using Todo.Web.Api;

namespace Todo.UnitTests.TestSupport;

/// <summary>In-memory stand-in for the Todo API used by bUnit tests. Records calls and can inject failures.</summary>
internal sealed class FakeTodoApiClient : ITodoApiClient
{
    private readonly List<TodoResponse> _items = [];

    public Func<Task<TodoListResponse>>? ListOverride { get; set; }
    public Exception? NextFailure { get; set; }
    public int ListCalls { get; private set; }
    public List<TodoRequest> Created { get; } = [];
    public List<(Guid Id, TodoRequest Request)> Updated { get; } = [];
    public List<Guid> Deleted { get; } = [];

    public TodoResponse Add(string title, bool isComplete = false, string? description = null)
    {
        var now = DateTimeOffset.UtcNow;
        var item = new TodoResponse(Guid.NewGuid(), title, description, isComplete, now, now);
        _items.Insert(0, item);
        return item;
    }

    public Task<TodoListResponse> ListAsync(int limit, string? continuationToken, CancellationToken cancellationToken = default)
    {
        ListCalls++;
        if (ListOverride is not null)
        {
            return ListOverride();
        }

        ThrowIfFailing();
        return Task.FromResult(new TodoListResponse([.. _items], null));
    }

    public Task<TodoResponse> CreateAsync(TodoRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        Created.Add(request);
        return Task.FromResult(Add(request.Title!.Trim(), request.IsComplete, request.Description));
    }

    public Task<TodoResponse> UpdateAsync(Guid id, TodoRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        Updated.Add((id, request));
        var index = _items.FindIndex(t => t.Id == id);
        if (index < 0)
        {
            throw new TodoApiException(404);
        }

        var updated = _items[index] with { Title = request.Title!, Description = request.Description, IsComplete = request.IsComplete };
        _items[index] = updated;
        return Task.FromResult(updated);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        Deleted.Add(id);
        _items.RemoveAll(t => t.Id == id);
        return Task.CompletedTask;
    }

    private void ThrowIfFailing()
    {
        if (NextFailure is { } failure)
        {
            NextFailure = null;
            throw failure;
        }
    }
}
