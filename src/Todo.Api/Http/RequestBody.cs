using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Todo.Contracts;

namespace Todo.Api.Http;

/// <summary>Result of reading a request body: either a value or an error response.</summary>
internal readonly record struct BodyReadResult<T>(T? Value, IActionResult? Error)
    where T : class;

/// <summary>Reads JSON request bodies with the 16 KB limit enforced before deserialization.</summary>
internal static class RequestBody
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<BodyReadResult<TodoRequest>> ReadTodoRequestAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ContentLength > TodoLimits.MaxRequestBodyBytes)
        {
            return new(null, Problems.PayloadTooLarge());
        }

        // Read at most limit + 1 bytes so chunked bodies without Content-Length are bounded too.
        var buffer = new byte[TodoLimits.MaxRequestBodyBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length &&
               (read = await request.Body.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken)) > 0)
        {
            total += read;
        }

        if (total > TodoLimits.MaxRequestBodyBytes)
        {
            return new(null, Problems.PayloadTooLarge());
        }

        if (total == 0)
        {
            return new(null, Problems.BadRequest("A JSON request body is required."));
        }

        try
        {
            var value = JsonSerializer.Deserialize<TodoRequest>(buffer.AsSpan(0, total), JsonOptions);
            return value is null
                ? new(null, Problems.BadRequest("A JSON object request body is required."))
                : new(value, null);
        }
        catch (JsonException)
        {
            return new(null, Problems.BadRequest("The request body is not valid JSON for a todo."));
        }
    }
}
