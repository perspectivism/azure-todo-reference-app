using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Todo.Api.Auth;
using Todo.Api.Todos;

namespace Todo.Api;

public enum StorageProvider
{
    /// <summary>Process-local store for unit tests and early local validation. Development only.</summary>
    InMemory,
}

/// <summary>Configuration section <c>Storage</c>.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public StorageProvider Provider { get; set; } = StorageProvider.InMemory;
}

public static class TodoApiServiceCollectionExtensions
{
    /// <summary>Registers the Todo API services. Throws on unsafe or incomplete configuration.</summary>
    public static IServiceCollection AddTodoApi(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddTodoApiAuthentication(configuration, environment);
        services.AddTodoStorage(configuration, environment);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TodoService>();
        return services;
    }

    public static IServiceCollection AddTodoApiAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var authOptions = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        AuthStartupGuard.Validate(authOptions, environment);

        services.AddSingleton(authOptions);
        services.AddScoped<CurrentUser>();

        if (authOptions.Mode == AuthMode.Dev)
        {
            services.AddAuthentication(DevAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, DevAuthenticationHandler>(DevAuthenticationHandler.SchemeName, _ => { });
            return services;
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authOptions.Authority;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = EntraTokenValidation.CreateParameters(authOptions);
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        var failure = EntraTokenValidation.GetClaimFailure(context.Principal, authOptions);
                        if (failure is not null)
                        {
                            context.Fail(failure);
                        }

                        return Task.CompletedTask;
                    },
                };
            });
        return services;
    }

    public static IServiceCollection AddTodoStorage(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var storageOptions = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();

        if (storageOptions.Provider == StorageProvider.InMemory && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"Storage:Provider 'InMemory' is allowed only when the host environment is Development (current: '{environment.EnvironmentName}').");
        }

        services.AddSingleton<ITodoRepository, InMemoryTodoRepository>();
        return services;
    }
}
