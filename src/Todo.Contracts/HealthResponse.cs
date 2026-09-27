namespace Todo.Contracts;

/// <summary>Body of the anonymous <c>GET /health</c> liveness response.</summary>
public sealed record HealthResponse(string Status)
{
    public const string Healthy = "healthy";
}
