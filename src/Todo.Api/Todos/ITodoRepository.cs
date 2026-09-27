namespace Todo.Api.Todos;

/// <summary>
/// Todo persistence. Every operation is scoped to the owning user id, which is always the
/// authenticated caller's object id. Implementations must never read or modify another user's items.
/// </summary>
public interface ITodoRepository
{
    Task<TodoItem?> GetAsync(string userId, string id, CancellationToken cancellationToken);

    /// <exception cref="InvalidContinuationTokenException">The continuation token is not valid.</exception>
    Task<TodoPage> ListAsync(string userId, int limit, string? continuationToken, CancellationToken cancellationToken);

    Task CreateAsync(TodoItem item, CancellationToken cancellationToken);

    /// <returns><c>false</c> when the item does not exist for <see cref="TodoItem.UserId"/>.</returns>
    Task<bool> ReplaceAsync(TodoItem item, CancellationToken cancellationToken);

    /// <returns><c>false</c> when the item does not exist for <paramref name="userId"/>.</returns>
    Task<bool> DeleteAsync(string userId, string id, CancellationToken cancellationToken);
}
