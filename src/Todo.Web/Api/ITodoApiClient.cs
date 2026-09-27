using Todo.Contracts;

namespace Todo.Web.Api;

/// <summary>
/// The Blazor app's only path to Todo data. Calls the Todo API (APIM in Azure, the local Functions host in development);
/// the web app never accesses Cosmos DB.
/// </summary>
public interface ITodoApiClient
{
    Task<TodoListResponse> ListAsync(int limit, string? continuationToken, CancellationToken cancellationToken = default);

    Task<TodoResponse> CreateAsync(TodoRequest request, CancellationToken cancellationToken = default);

    Task<TodoResponse> UpdateAsync(Guid id, TodoRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>A non-success response from the Todo API. Carries field errors for 400 validation responses.</summary>
public sealed class TodoApiException(int statusCode, IReadOnlyDictionary<string, string[]>? errors = null)
    : Exception($"The Todo API returned status code {statusCode}.")
{
    public int StatusCode { get; } = statusCode;

    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
}

/// <summary>The user's session must be re-established (for example, the token cache was lost after an app restart).</summary>
public sealed class ReauthenticationRequiredException(Exception? innerException = null)
    : Exception("Sign-in is required to call the Todo API.", innerException);
