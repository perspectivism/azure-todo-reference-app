using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using Todo.Api.Todos;

namespace Todo.Api.Http;

/// <summary>
/// Maps known exceptions to 400 problem responses and anything else to a generic 500 problem response.
/// Exceptions are logged (and exported as exception telemetry in Azure); details are never returned to the caller.
/// </summary>
internal sealed partial class ProblemHandlingMiddleware(ILogger<ProblemHandlingMiddleware> logger) : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            context.GetInvocationResult().Value = ToProblem(ex, context.FunctionDefinition.Name);
        }
    }

    /// <summary>Returns the problem response for an exception thrown by a function; unexpected exceptions are logged.</summary>
    internal IActionResult ToProblem(Exception exception, string functionName)
    {
        if (Find<TodoValidationException>(exception) is { } validation)
        {
            return Problems.Validation(validation.Errors);
        }

        if (Find<InvalidContinuationTokenException>(exception) is not null)
        {
            return Problems.Validation(
                new Dictionary<string, string[]> { ["continuationToken"] = ["The continuation token is not valid."] });
        }

        LogUnhandled(exception, functionName);
        return Problems.Unexpected();
    }

    private static T? Find<T>(Exception exception)
        where T : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception in function {FunctionName}")]
    private partial void LogUnhandled(Exception exception, string functionName);
}
