using Microsoft.Extensions.Hosting;

namespace Todo.Api.Auth;

/// <summary>Startup checks that fail fast on unsafe or incomplete authentication configuration.</summary>
public static class AuthStartupGuard
{
    public static void Validate(AuthOptions options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        switch (options.Mode)
        {
            case AuthMode.Dev when !environment.IsDevelopment():
                throw new InvalidOperationException(
                    $"Auth:Mode 'Dev' is allowed only when the host environment is Development (current: '{environment.EnvironmentName}').");

            case AuthMode.Entra:
                if (!Guid.TryParse(options.TenantId, out _))
                {
                    throw new InvalidOperationException("Auth:TenantId must be set to the Entra tenant id (a GUID) when Auth:Mode is 'Entra'.");
                }

                if (!Guid.TryParse(options.ClientId, out _))
                {
                    throw new InvalidOperationException("Auth:ClientId must be set to the todo-api application (client) id (a GUID) when Auth:Mode is 'Entra'.");
                }

                if (string.IsNullOrWhiteSpace(options.RequiredScope))
                {
                    throw new InvalidOperationException("Auth:RequiredScope must not be empty.");
                }

                break;

            case AuthMode.Dev:
                break;

            default:
                throw new InvalidOperationException($"Unsupported Auth:Mode '{options.Mode}'.");
        }
    }
}
