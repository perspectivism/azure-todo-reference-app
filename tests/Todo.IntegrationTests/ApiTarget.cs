using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Todo.Contracts;

namespace Todo.IntegrationTests;

/// <summary>
/// Integration-test configuration read from environment variables (set by scripts/run-integration-tests.ps1):
/// <list type="bullet">
/// <item><c>TODOAPP_API_BASE_URL</c>: API base URL. When unset, every test is skipped so a plain <c>dotnet test</c> needs no running API.</item>
/// <item><c>TODOAPP_TARGET</c>: <c>Local</c> (default) or <c>Azure</c>.</item>
/// <item><c>TODOAPP_LOCAL_AUTH_MODE</c>: <c>Dev</c> (default) or <c>Entra</c>; the Auth:Mode of the local Functions host.</item>
/// <item><c>TODOAPP_TOKEN_A</c>, <c>TODOAPP_TOKEN_B</c>: access tokens for two different users (Azure only, never committed).</item>
/// </list>
/// </summary>
public static class ApiTarget
{
    public const string NotConfiguredReason =
        "TODOAPP_API_BASE_URL is not set. Run: pwsh ./scripts/run-integration-tests.ps1 -Target Local|Azure";

    public const string NoUserReason =
        "Requires an authenticated user: a local host in Auth:Mode=Dev, or TODOAPP_TOKEN_A for Azure.";

    public const string NoSecondUserReason =
        "Requires two users: a local host in Auth:Mode=Dev, or TODOAPP_TOKEN_A and TODOAPP_TOKEN_B for Azure. The user-isolation gate remains open.";

    public const string NoAnonymousRejectionReason =
        "Anonymous rejection is tested against Azure or a local host in Auth:Mode=Entra (a Dev-mode host falls back to the development user).";

    // Dev-mode identities are fresh per run so repeated runs against a persistent store do not interfere.
    private static readonly string DevUserA = Guid.NewGuid().ToString();
    private static readonly string DevUserB = Guid.NewGuid().ToString();

    public static Uri? BaseUrl { get; } =
        Uri.TryCreate(Environment.GetEnvironmentVariable("TODOAPP_API_BASE_URL"), UriKind.Absolute, out var uri)
            ? new Uri(uri.AbsoluteUri.TrimEnd('/') + "/")
            : null;

    public static bool IsAzure { get; } =
        string.Equals(Environment.GetEnvironmentVariable("TODOAPP_TARGET"), "Azure", StringComparison.OrdinalIgnoreCase);

    public static bool IsLocalDevMode { get; } = !IsAzure &&
        !string.Equals(Environment.GetEnvironmentVariable("TODOAPP_LOCAL_AUTH_MODE"), "Entra", StringComparison.OrdinalIgnoreCase);

    private static string? TokenA => Environment.GetEnvironmentVariable("TODOAPP_TOKEN_A") is { Length: > 0 } t ? t : null;

    private static string? TokenB => Environment.GetEnvironmentVariable("TODOAPP_TOKEN_B") is { Length: > 0 } t ? t : null;

    public static bool IsConfigured => BaseUrl is not null;

    public static bool HasUserA => IsConfigured && (IsAzure ? TokenA is not null : IsLocalDevMode);

    public static bool HasTwoUsers => HasUserA && (!IsAzure || TokenB is not null);

    public static bool CanTestAnonymousRejection => IsConfigured && (IsAzure || !IsLocalDevMode);

    public static HttpClient CreateAnonymousClient() => new() { BaseAddress = BaseUrl, Timeout = TimeSpan.FromSeconds(30) };

    public static HttpClient CreateClientForUserA() => CreateUserClient(IsAzure ? TokenA : null, DevUserA);

    public static HttpClient CreateClientForUserB() => CreateUserClient(IsAzure ? TokenB : null, DevUserB);

    private static HttpClient CreateUserClient(string? token, string devUserId)
    {
        var client = CreateAnonymousClient();
        if (IsAzure)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            client.DefaultRequestHeaders.Add(DevIdentity.UserIdHeader, devUserId);
        }

        return client;
    }
}

/// <summary>Runs when an API base URL is configured.</summary>
public sealed class ApiFactAttribute : FactAttribute
{
    public ApiFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = ApiTarget.NotConfiguredReason;
        SkipUnless = nameof(ApiTarget.IsConfigured);
        SkipType = typeof(ApiTarget);
    }
}

/// <summary>Runs when user A can be authenticated.</summary>
public sealed class UserFactAttribute : FactAttribute
{
    public UserFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = ApiTarget.NoUserReason;
        SkipUnless = nameof(ApiTarget.HasUserA);
        SkipType = typeof(ApiTarget);
    }
}

/// <summary>Runs when two distinct users can be authenticated.</summary>
public sealed class TwoUserFactAttribute : FactAttribute
{
    public TwoUserFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = ApiTarget.NoSecondUserReason;
        SkipUnless = nameof(ApiTarget.HasTwoUsers);
        SkipType = typeof(ApiTarget);
    }
}

/// <summary>Runs when the target rejects anonymous requests (Azure, or a local Entra-mode host).</summary>
public sealed class AnonymousRejectionFactAttribute : FactAttribute
{
    public AnonymousRejectionFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = ApiTarget.NoAnonymousRejectionReason;
        SkipUnless = nameof(ApiTarget.CanTestAnonymousRejection);
        SkipType = typeof(ApiTarget);
    }
}
