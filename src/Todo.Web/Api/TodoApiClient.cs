using System.Globalization;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Todo.Contracts;

namespace Todo.Web.Api;

/// <summary>
/// Adds the signed-in user's identity to an outgoing API request: a bearer access token in Entra mode,
/// or the development user id header in Dev mode. Implementations must never log the token.
/// </summary>
public interface ITodoApiRequestAuthorizer
{
    Task AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}

/// <summary>Typed HttpClient for the Todo API.</summary>
public sealed class TodoApiClient(HttpClient httpClient, ITodoApiRequestAuthorizer authorizer) : ITodoApiClient
{
    public async Task<TodoListResponse> ListAsync(int limit, string? continuationToken, CancellationToken cancellationToken = default)
    {
        var url = $"todos?limit={limit.ToString(CultureInfo.InvariantCulture)}";
        if (!string.IsNullOrEmpty(continuationToken))
        {
            url += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        return await SendAsync<TodoListResponse>(request, cancellationToken);
    }

    public async Task<TodoResponse> CreateAsync(TodoRequest request, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "todos") { Content = JsonContent.Create(request) };
        return await SendAsync<TodoResponse>(message, cancellationToken);
    }

    public async Task<TodoResponse> UpdateAsync(Guid id, TodoRequest request, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, $"todos/{id:D}") { Content = JsonContent.Create(request) };
        return await SendAsync<TodoResponse>(message, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Delete, $"todos/{id:D}");
        using var response = await SendCoreAsync(message, cancellationToken);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(request, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new TodoApiException((int)response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await authorizer.AuthorizeAsync(request, cancellationToken);
        var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            IReadOnlyDictionary<string, string[]>? errors = null;
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                try
                {
                    errors = (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(cancellationToken))?.Errors.AsReadOnly();
                }
                catch (System.Text.Json.JsonException)
                {
                    errors = null;
                }
            }

            throw new TodoApiException((int)response.StatusCode, errors);
        }
    }
}
