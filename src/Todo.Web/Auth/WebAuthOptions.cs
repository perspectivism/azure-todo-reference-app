namespace Todo.Web.Auth;

public enum WebAuthMode
{
    /// <summary>Microsoft Entra ID sign-in (OpenID Connect) with delegated API tokens (default).</summary>
    Entra,

    /// <summary>Development-only fixed user. Rejected outside Development.</summary>
    Dev,
}

/// <summary>Configuration section <c>Auth</c> of the web app.</summary>
public sealed class WebAuthOptions
{
    public const string SectionName = "Auth";

    public WebAuthMode Mode { get; set; } = WebAuthMode.Entra;
}

/// <summary>Configuration section <c>TodoApi</c>.</summary>
public sealed class TodoApiOptions
{
    public const string SectionName = "TodoApi";

    /// <summary>API base URL: the APIM gateway in Azure, the local Functions host in development.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Delegated scope requested for API calls in Entra mode, for example api://{todo-api client id}/access_as_user.</summary>
    public string? Scope { get; set; }
}

/// <summary>Claim types read from the signed-in user (raw OIDC names; inbound claim mapping is disabled).</summary>
public static class WebClaims
{
    public const string ObjectId = "oid";
    public const string Name = "name";
    public const string SignInName = "preferred_username";
}
