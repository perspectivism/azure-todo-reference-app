using System.Collections.Concurrent;
using System.Globalization;

namespace Todo.Api.Todos;

/// <summary>
/// Process-local store used by unit tests and early local validation (Storage:Provider=InMemory, Development only).
/// Not a supported runtime mode; data is lost when the process stops.
/// </summary>
public sealed class InMemoryTodoRepository : ITodoRepository
{
    private readonly ConcurrentDictionary<(string UserId, string Id), TodoItem> _items = new();

    public Task<TodoItem?> GetAsync(string userId, string id, CancellationToken cancellationToken)
        => Task.FromResult(_items.TryGetValue((userId, id), out var item) ? item : null);

    public Task<TodoPage> ListAsync(string userId, int limit, string? continuationToken, CancellationToken cancellationToken)
    {
        var offset = 0;
        if (continuationToken is not null &&
            (!int.TryParse(continuationToken, NumberStyles.None, CultureInfo.InvariantCulture, out offset) || offset <= 0))
        {
            throw new InvalidContinuationTokenException();
        }

        var owned = _items.Values
            .Where(i => i.UserId == userId)
            .OrderByDescending(i => i.CreatedUtc)
            .ThenByDescending(i => i.Id, StringComparer.Ordinal)
            .ToList();

        var page = owned.Skip(offset).Take(limit).ToList();
        var next = offset + page.Count;
        var token = next < owned.Count ? next.ToString(CultureInfo.InvariantCulture) : null;
        return Task.FromResult(new TodoPage(page, token));
    }

    public Task CreateAsync(TodoItem item, CancellationToken cancellationToken)
    {
        if (!_items.TryAdd((item.UserId, item.Id), item))
        {
            throw new InvalidOperationException("A todo with this id already exists.");
        }

        return Task.CompletedTask;
    }

    public Task<bool> ReplaceAsync(TodoItem item, CancellationToken cancellationToken)
    {
        var key = (item.UserId, item.Id);
        return Task.FromResult(_items.TryGetValue(key, out var existing) && _items.TryUpdate(key, item, existing));
    }

    public Task<bool> DeleteAsync(string userId, string id, CancellationToken cancellationToken)
        => Task.FromResult(_items.TryRemove((userId, id), out _));
}
