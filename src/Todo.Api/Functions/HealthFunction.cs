using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Todo.Contracts;

namespace Todo.Api.Functions;

/// <summary>Shallow liveness check. Does not touch Cosmos DB and returns no sensitive data.</summary>
public sealed class HealthFunction
{
    public const string FunctionName = "Health";

    [Function(FunctionName)]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest request)
        => new OkObjectResult(new HealthResponse(HealthResponse.Healthy));
}
