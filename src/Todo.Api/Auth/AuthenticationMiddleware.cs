using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Todo.Api.Functions;
using Todo.Api.Http;

namespace Todo.Api.Auth;

/// <summary>
/// Requires an authenticated identity for every function except the anonymous health check.
/// Functions use AuthorizationLevel.Anonymous because function keys are credentials; the bearer token is the gate.
/// </summary>
internal sealed partial class AuthenticationMiddleware(ILogger<AuthenticationMiddleware> logger) : IFunctionsWorkerMiddleware
{
    private static readonly HashSet<string> AnonymousFunctions = new(StringComparer.Ordinal) { HealthFunction.FunctionName };

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpContext = context.GetHttpContext()
            ?? throw new InvalidOperationException("Only HTTP-triggered functions are supported.");

        var rejection = await AuthenticateAsync(
            context.FunctionDefinition.Name,
            httpContext,
            context.InstanceServices.GetRequiredService<CurrentUser>());
        if (rejection is not null)
        {
            context.GetInvocationResult().Value = rejection;
            return;
        }

        await next(context);
    }

    /// <summary>
    /// Returns null when the invocation may run (an anonymous function, or an authenticated caller whose object id has
    /// been set on <paramref name="currentUser"/>); otherwise the 401 problem response to return instead.
    /// </summary>
    internal async Task<IActionResult?> AuthenticateAsync(string functionName, HttpContext httpContext, CurrentUser currentUser)
    {
        if (AnonymousFunctions.Contains(functionName))
        {
            return null;
        }

        var userId = await CurrentUser.AuthenticateAsync(httpContext);
        if (userId is null)
        {
            // Never log the Authorization header or token; only whether one was present.
            LogRejected(functionName, httpContext.Request.Headers.Authorization.Count > 0);
            httpContext.Response.Headers.WWWAuthenticate = "Bearer";
            return Problems.Unauthorized();
        }

        currentUser.Set(userId);
        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected unauthenticated request to {FunctionName} (authorization header present: {HasAuthorizationHeader})")]
    private partial void LogRejected(string functionName, bool hasAuthorizationHeader);
}
