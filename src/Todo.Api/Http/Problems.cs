using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Todo.Api.Http;

/// <summary>RFC 9457 problem responses (application/problem+json). Never include stack traces or internal details.</summary>
internal static class Problems
{
    public static ObjectResult BadRequest(string detail) =>
        Create(StatusCodes.Status400BadRequest, "Bad Request", detail, "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.1");

    public static ObjectResult Validation(IReadOnlyDictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetails(errors.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Type = "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.1",
        };
        return WithTrace(problem);
    }

    public static ObjectResult Unauthorized() =>
        Create(StatusCodes.Status401Unauthorized, "Unauthorized", "A valid access token is required.", "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.2");

    public static ObjectResult NotFound() =>
        Create(StatusCodes.Status404NotFound, "Not Found", "The requested resource was not found.", "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.5");

    public static ObjectResult PayloadTooLarge() =>
        Create(StatusCodes.Status413PayloadTooLarge, "Payload Too Large", "The request body exceeds the 16 KB limit.", "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.14");

    public static ObjectResult Unexpected() =>
        Create(StatusCodes.Status500InternalServerError, "Internal Server Error", "An unexpected error occurred.", "https://www.rfc-editor.org/rfc/rfc9110#section-15.6.1");

    private static ObjectResult Create(int status, string title, string detail, string type) =>
        WithTrace(new ProblemDetails { Status = status, Title = title, Detail = detail, Type = type });

    private static ObjectResult WithTrace(ProblemDetails problem)
    {
        if (Activity.Current is { } activity)
        {
            problem.Extensions["traceId"] = activity.TraceId.ToString();
        }

        var result = new ObjectResult(problem) { StatusCode = problem.Status };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}
