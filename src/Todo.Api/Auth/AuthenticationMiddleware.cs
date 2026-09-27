using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Todo.Api.Functions;
using Todo.Api.Http;

namespace Todo.Api.Auth;

/// <summary>
/// Requires an authenticated identity for every function except the anonymous health check.
/// Functions use AuthorizationLevel.Anonymous because function keys are credentials; the bearer token is the gate.
/// </summary>
internal sealed class AuthenticationMiddleware : IFunctionsWorkerMiddleware
{
    private static readonly HashSet<string> AnonymousFunctions = new(StringComparer.Ordinal) { HealthFunction.FunctionName };

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        if (AnonymousFunctions.Contains(context.FunctionDefinition.Name))
        {
            await next(context);
            return;
        }

        var httpContext = context.GetHttpContext()
            ?? throw new InvalidOperationException("Only HTTP-triggered functions are supported.");

        var userId = await CurrentUser.AuthenticateAsync(httpContext);
        if (userId is null)
        {
            httpContext.Response.Headers.WWWAuthenticate = "Bearer";
            context.GetInvocationResult().Value = Problems.Unauthorized();
            return;
        }

        context.InstanceServices.GetRequiredService<CurrentUser>().Set(userId);
        await next(context);
    }
}
