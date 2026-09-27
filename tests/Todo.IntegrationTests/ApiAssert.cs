using System.Net;
using System.Net.Http.Json;
using Todo.Contracts;

namespace Todo.IntegrationTests;

internal static class ApiAssert
{
    public static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
    }

    public static async Task<TodoResponse> CreateAsync(HttpClient client, TodoRequest request)
    {
        using var response = await client.PostAsJsonAsync("todos", request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var todo = await response.Content.ReadFromJsonAsync<TodoResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(todo);
        return todo;
    }
}
