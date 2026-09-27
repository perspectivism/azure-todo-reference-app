namespace Todo.Contracts;

/// <summary>A todo as returned by the API. The owner's user id is never returned.</summary>
public sealed record TodoResponse(
    Guid Id,
    string Title,
    string? Description,
    bool IsComplete,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>Body of <c>GET /todos</c>. <see cref="ContinuationToken"/> is null on the last page.</summary>
public sealed record TodoListResponse(IReadOnlyList<TodoResponse> Items, string? ContinuationToken);
