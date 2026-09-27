using Todo.Contracts;

namespace Todo.Api.Todos;

/// <summary>Stored todo. <see cref="UserId"/> is the owner's Entra object id and the partition key.</summary>
public sealed record TodoItem
{
    public required string Id { get; init; }
    public required string UserId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public bool IsComplete { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }

    public TodoResponse ToResponse() => new(Guid.Parse(Id), Title, Description, IsComplete, CreatedUtc, UpdatedUtc);
}

/// <summary>One page of a user's todos, newest first.</summary>
public sealed record TodoPage(IReadOnlyList<TodoItem> Items, string? ContinuationToken);
