using Microsoft.Extensions.Hosting;
using Todo.Contracts;

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
            case AuthMode.Dev:
                DevIdentity.EnsureAllowed(environment.IsDevelopment(), environment.EnvironmentName);
                break;

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

            default:
                throw new InvalidOperationException($"Unsupported Auth:Mode '{options.Mode}'.");
        }
    }
}
