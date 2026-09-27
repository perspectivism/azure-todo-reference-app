namespace Todo.Contracts;

/// <summary>
/// Development-only identity mode (<c>Auth:Mode=Dev</c>) shared by the API and the Blazor app.
/// Allowed only when the host environment is Development; never used in Azure.
/// </summary>
public static class DevIdentity
{
    /// <summary>Header carrying the caller's user id (a GUID) in Dev mode.</summary>
    public const string UserIdHeader = "X-Dev-User-Id";

    /// <summary>Fixed development user used when no header is supplied.</summary>
    public const string DefaultUserId = "00000000-0000-0000-0000-000000000001";

    public const string DefaultUserName = "Development User";

    public const string DefaultSignInName = "dev.user@localhost";

    /// <summary>
    /// Startup guard shared by the API and the web app: throws when Auth:Mode is 'Dev' and the host environment is
    /// not Development, so the development identity can never be enabled in a deployed environment.
    /// </summary>
    public static void EnsureAllowed(bool isDevelopmentEnvironment, string environmentName)
    {
        if (!isDevelopmentEnvironment)
        {
            throw new InvalidOperationException(
                $"Auth:Mode 'Dev' is allowed only when the host environment is Development (current: '{environmentName}').");
        }
    }
}
