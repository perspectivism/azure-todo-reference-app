using System.Text.Json;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Todo.Api.Auth;
using Todo.Api.Todos;

namespace Todo.Api;

public enum StorageProvider
{
    /// <summary>Azure Cosmos DB for NoSQL (default; the Cosmos DB Emulator locally).</summary>
    Cosmos,

    /// <summary>Process-local store for unit tests and early local validation. Development only.</summary>
    InMemory,
}

/// <summary>Configuration section <c>Storage</c>.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public StorageProvider Provider { get; set; } = StorageProvider.Cosmos;
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

        if (storageOptions.Provider == StorageProvider.InMemory)
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"Storage:Provider 'InMemory' is allowed only when the host environment is Development (current: '{environment.EnvironmentName}').");
            }

            services.AddSingleton<ITodoRepository, InMemoryTodoRepository>();
            return services;
        }

        var cosmosOptions = configuration.GetSection(CosmosOptions.SectionName).Get<CosmosOptions>() ?? new CosmosOptions();
        ValidateCosmosOptions(cosmosOptions, environment);

        services.AddSingleton(cosmosOptions);
        services.AddSingleton(_ => CreateCosmosClient(cosmosOptions));
        services.AddSingleton<CosmosContainerProvider>();
        services.AddSingleton<ITodoRepository, CosmosTodoRepository>();
        return services;
    }

    internal static void ValidateCosmosOptions(CosmosOptions options, IHostEnvironment environment)
    {
        var hasConnectionString = !string.IsNullOrWhiteSpace(options.ConnectionString);
        var hasEndpoint = Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _);

        if (!hasConnectionString && !hasEndpoint)
        {
            throw new InvalidOperationException(
                "Cosmos DB is not configured. For local development, copy the Azure Cosmos DB Emulator connection string from " +
                $"{CosmosOptions.EmulatorDocumentationUrl} (Authentication section) into Cosmos__ConnectionString in " +
                "src/Todo.Api/local.settings.json (ignored by Git). In Azure, Cosmos:Endpoint is set by the Bicep deployment.");
        }

        if (options.AutoCreate && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Cosmos:AutoCreate is allowed only in Development. In Azure, Bicep creates the database and container.");
        }
    }

    private static CosmosClient CreateCosmosClient(CosmosOptions options)
    {
        var clientOptions = new CosmosClientOptions
        {
            ApplicationName = "todo-api",
            UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web),
        };

        // Local emulator: connection string from untracked configuration. Azure: managed identity (key auth disabled).
        return !string.IsNullOrWhiteSpace(options.ConnectionString)
            ? new CosmosClient(options.ConnectionString, clientOptions)
            : new CosmosClient(options.Endpoint, new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned), clientOptions);
    }
}
