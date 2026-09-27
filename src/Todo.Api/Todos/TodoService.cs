using Microsoft.Extensions.Logging;
using Todo.Contracts;

namespace Todo.Api.Todos;

/// <summary>
/// Todo business rules. The caller's user id always comes from the validated identity, never from the request.
/// </summary>
public sealed partial class TodoService(ITodoRepository repository, TimeProvider timeProvider, ILogger<TodoService> logger)
{
    public async Task<TodoListResponse> ListAsync(string userId, int limit, string? continuationToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, TodoLimits.MaxPageSize);

        var page = await repository.ListAsync(userId, limit, continuationToken, cancellationToken);
        return new TodoListResponse([.. page.Items.Select(i => i.ToResponse())], page.ContinuationToken);
    }

    public async Task<TodoResponse?> GetAsync(string userId, string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (!TryNormalizeId(id, out var normalizedId))
        {
            return null;
        }

        var item = await repository.GetAsync(userId, normalizedId, cancellationToken);
        return item?.ToResponse();
    }

    public async Task<TodoResponse> CreateAsync(string userId, TodoRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        EnsureValid(request);

        var now = timeProvider.GetUtcNow();
        var item = new TodoItem
        {
            Id = Guid.NewGuid().ToString("D"),
            UserId = userId,
            Title = request.Title!.Trim(),
            Description = NormalizeDescription(request.Description),
            IsComplete = request.IsComplete,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        await repository.CreateAsync(item, cancellationToken);
        LogCreated(item.Id, userId);
        return item.ToResponse();
    }

    public async Task<TodoResponse?> UpdateAsync(string userId, string id, TodoRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (!TryNormalizeId(id, out var normalizedId))
        {
            return null;
        }

        EnsureValid(request);

        var existing = await repository.GetAsync(userId, normalizedId, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var updated = existing with
        {
            Title = request.Title!.Trim(),
            Description = NormalizeDescription(request.Description),
            IsComplete = request.IsComplete,
            UpdatedUtc = timeProvider.GetUtcNow(),
        };

        if (!await repository.ReplaceAsync(updated, cancellationToken))
        {
            return null;
        }

        LogUpdated(updated.Id, userId);
        return updated.ToResponse();
    }

    public async Task<bool> DeleteAsync(string userId, string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (!TryNormalizeId(id, out var normalizedId))
        {
            return false;
        }

        var deleted = await repository.DeleteAsync(userId, normalizedId, cancellationToken);
        if (deleted)
        {
            LogDeleted(normalizedId, userId);
        }

        return deleted;
    }

    /// <summary>Ids are GUIDs stored in lowercase "D" format. Anything else is treated as not found.</summary>
    internal static bool TryNormalizeId(string? id, out string normalizedId)
    {
        if (Guid.TryParseExact(id, "D", out var guid))
        {
            normalizedId = guid.ToString("D");
            return true;
        }

        normalizedId = string.Empty;
        return false;
    }

    private static void EnsureValid(TodoRequest request)
    {
        var errors = TodoValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new TodoValidationException(errors);
        }
    }

    private static string? NormalizeDescription(string? description)
        => string.IsNullOrWhiteSpace(description) ? null : description;

    // Log ids only. Titles and descriptions are user content and are never logged.
    [LoggerMessage(Level = LogLevel.Information, Message = "Created todo {TodoId} for user {UserId}")]
    private partial void LogCreated(string todoId, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updated todo {TodoId} for user {UserId}")]
    private partial void LogUpdated(string todoId, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted todo {TodoId} for user {UserId}")]
    private partial void LogDeleted(string todoId, string userId);
}
