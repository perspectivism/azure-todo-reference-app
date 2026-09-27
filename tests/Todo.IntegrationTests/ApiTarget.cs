namespace Todo.IntegrationTests;

/// <summary>
/// Integration-test configuration read from environment variables. Set by scripts/run-integration-tests.ps1.
/// Tests are skipped when <c>TODOAPP_API_BASE_URL</c> is not set, so a plain <c>dotnet test</c> does not require a running API.
/// </summary>
public static class ApiTarget
{
    public const string NotConfiguredReason =
        "TODOAPP_API_BASE_URL is not set. Run: pwsh ./scripts/run-integration-tests.ps1 -Target Local|Azure";

    public static Uri? BaseUrl { get; } =
        Uri.TryCreate(Environment.GetEnvironmentVariable("TODOAPP_API_BASE_URL"), UriKind.Absolute, out var uri)
            ? new Uri(uri.AbsoluteUri.TrimEnd('/') + "/")
            : null;

    public static bool IsConfigured => BaseUrl is not null;

    public static HttpClient CreateAnonymousClient() => new() { BaseAddress = BaseUrl, Timeout = TimeSpan.FromSeconds(30) };
}
