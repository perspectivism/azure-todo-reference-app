using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Todo.Api.Auth;
using Todo.Api.Http;

namespace Todo.Api.Functions;

/// <summary>Configuration section <c>Diagnostics</c>.</summary>
public sealed class DiagnosticsOptions
{
    public const string SectionName = "Diagnostics";

    /// <summary>Enables POST /diagnostics/fault. False by default in every environment; enabled only temporarily for verification.</summary>
    public bool EnableFaultInjection { get; set; }
}

/// <summary>Fault injection for verifying exception telemetry. Requires authentication; returns 404 unless enabled.</summary>
public sealed partial class DiagnosticsFunctions(DiagnosticsOptions options, CurrentUser currentUser, ILogger<DiagnosticsFunctions> logger)
{
    public const string FaultMessage = "Deliberate fault injected for exception-telemetry verification.";

    [Function("TriggerFault")]
    public IActionResult TriggerFault(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "diagnostics/fault")] HttpRequest request)
    {
        if (!options.EnableFaultInjection)
        {
            return Problems.NotFound();
        }

        LogFaultInjected(currentUser.UserId);
        throw new InvalidOperationException(FaultMessage);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fault injection requested by user {UserId}")]
    private partial void LogFaultInjected(string userId);
}
