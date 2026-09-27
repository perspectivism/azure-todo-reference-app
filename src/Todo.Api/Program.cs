using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using Todo.Api;
using Todo.Api.Auth;
using Todo.Api.Http;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Order matters: problem handling wraps authentication so that any failure becomes a ProblemDetails response.
builder.UseMiddleware<ProblemHandlingMiddleware>();
builder.UseMiddleware<AuthenticationMiddleware>();

builder.Services.AddTodoApi(builder.Configuration, builder.Environment);

if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

builder.Build().Run();
