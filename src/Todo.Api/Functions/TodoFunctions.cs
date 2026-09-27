using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Primitives;
using Todo.Api.Auth;
using Todo.Api.Http;
using Todo.Api.Todos;
using Todo.Contracts;

namespace Todo.Api.Functions;

/// <summary>
/// Thin HTTP endpoints for the Todo API. Authentication is enforced by <see cref="AuthenticationMiddleware"/>;
/// the owner is always <see cref="CurrentUser.UserId"/>, never a value from the URL, query, or body.
/// </summary>
public sealed class TodoFunctions(TodoService todos, CurrentUser currentUser)
{
    [Function("ListTodos")]
    public async Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "todos")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParseLimit(request.Query["limit"], out var limit))
        {
            return Problems.Validation(new Dictionary<string, string[]>
            {
                ["limit"] = [$"limit must be an integer between 1 and {TodoLimits.MaxPageSize}."],
            });
        }

        var continuationToken = request.Query["continuationToken"].ToString();
        var result = await todos.ListAsync(
            currentUser.UserId,
            limit,
            string.IsNullOrEmpty(continuationToken) ? null : continuationToken,
            cancellationToken);
        return new OkObjectResult(result);
    }

    [Function("GetTodo")]
    public async Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "todos/{id}")] HttpRequest request,
        string id,
        CancellationToken cancellationToken)
    {
        var todo = await todos.GetAsync(currentUser.UserId, id, cancellationToken);
        return todo is null ? Problems.NotFound() : new OkObjectResult(todo);
    }

    [Function("CreateTodo")]
    public async Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "todos")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var body = await RequestBody.ReadTodoRequestAsync(request, cancellationToken);
        if (body.Error is not null)
        {
            return body.Error;
        }

        var created = await todos.CreateAsync(currentUser.UserId, body.Value!, cancellationToken);
        return new CreatedResult($"/todos/{created.Id:D}", created);
    }

    [Function("UpdateTodo")]
    public async Task<IActionResult> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "todos/{id}")] HttpRequest request,
        string id,
        CancellationToken cancellationToken)
    {
        var body = await RequestBody.ReadTodoRequestAsync(request, cancellationToken);
        if (body.Error is not null)
        {
            return body.Error;
        }

        var updated = await todos.UpdateAsync(currentUser.UserId, id, body.Value!, cancellationToken);
        return updated is null ? Problems.NotFound() : new OkObjectResult(updated);
    }

    [Function("DeleteTodo")]
    public async Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "todos/{id}")] HttpRequest request,
        string id,
        CancellationToken cancellationToken)
    {
        var deleted = await todos.DeleteAsync(currentUser.UserId, id, cancellationToken);
        return deleted ? new NoContentResult() : Problems.NotFound();
    }

    internal static bool TryParseLimit(StringValues values, out int limit)
    {
        limit = TodoLimits.DefaultPageSize;
        if (values.Count == 0)
        {
            return true;
        }

        return values.Count == 1
            && int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit)
            && limit is >= 1 and <= TodoLimits.MaxPageSize;
    }
}
