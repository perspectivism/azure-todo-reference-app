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
        catch (Exception ex) when (Find<TodoValidationException>(ex) is { } validation)
        {
            context.GetInvocationResult().Value = Problems.Validation(validation.Errors);
        }
        catch (Exception ex) when (Find<InvalidContinuationTokenException>(ex) is not null)
        {
            context.GetInvocationResult().Value = Problems.Validation(
                new Dictionary<string, string[]> { ["continuationToken"] = ["The continuation token is not valid."] });
        }
        catch (Exception ex)
        {
            LogUnhandled(ex, context.FunctionDefinition.Name);
            context.GetInvocationResult().Value = Problems.Unexpected();
        }
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
