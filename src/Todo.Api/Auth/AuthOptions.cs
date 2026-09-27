namespace Todo.Api.Auth;

public enum AuthMode
{
    /// <summary>Microsoft Entra ID bearer tokens (default).</summary>
    Entra,

    /// <summary>Development-only identity from the X-Dev-User-Id header. Rejected outside Development.</summary>
    Dev,
}

/// <summary>Configuration section <c>Auth</c>.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public AuthMode Mode { get; set; } = AuthMode.Entra;

    /// <summary>Entra authority host, for example https://login.microsoftonline.com/.</summary>
    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    /// <summary>Directory (tenant) id that issued tokens must belong to.</summary>
    public string? TenantId { get; set; }

    /// <summary>Application (client) id of the <c>todo-api</c> registration. Tokens must be issued for this audience.</summary>
    public string? ClientId { get; set; }

    /// <summary>Delegated scope that must be present in the <c>scp</c> claim.</summary>
    public string RequiredScope { get; set; } = "access_as_user";

    public string Authority => $"{Instance.TrimEnd('/')}/{TenantId}/v2.0";

    /// <summary>v2.0 token issuer for the configured tenant.</summary>
    public string Issuer => $"{Instance.TrimEnd('/')}/{TenantId}/v2.0";
}
