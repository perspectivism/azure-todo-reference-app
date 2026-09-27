using Microsoft.AspNetCore.Authentication;
using Todo.Web.Api;
using Todo.Web.Auth;

namespace Todo.Web;

public static class TodoWebServiceCollectionExtensions
{
    /// <summary>Registers authentication and the Todo API client. Throws on unsafe or incomplete configuration.</summary>
    public static IServiceCollection AddTodoWeb(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var authOptions = configuration.GetSection(WebAuthOptions.SectionName).Get<WebAuthOptions>() ?? new WebAuthOptions();
        var apiOptions = configuration.GetSection(TodoApiOptions.SectionName).Get<TodoApiOptions>() ?? new TodoApiOptions();
        ValidateStartup(authOptions, apiOptions, environment, configuration.GetSection(EntraWebAuthentication.AzureAdSection));

        services.AddSingleton(authOptions);
        services.AddSingleton(apiOptions);
        services.AddCascadingAuthenticationState();
        services.AddAuthorization();

        switch (authOptions.Mode)
        {
            case WebAuthMode.Dev:
                services.AddAuthentication(DevWebAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, DevWebAuthenticationHandler>(DevWebAuthenticationHandler.SchemeName, _ => { });
                services.AddScoped<ITodoApiRequestAuthorizer, DevTodoApiRequestAuthorizer>();
                break;

            case WebAuthMode.Entra:
                EntraWebAuthentication.Add(services, configuration, apiOptions);
                break;

            default:
                throw new InvalidOperationException($"Unsupported Auth:Mode '{authOptions.Mode}'.");
        }

        var baseUrl = apiOptions.BaseUrl!.TrimEnd('/') + "/";
        services.AddHttpClient<ITodoApiClient, TodoApiClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }

    internal static void ValidateStartup(WebAuthOptions authOptions, TodoApiOptions apiOptions, IHostEnvironment environment, IConfiguration? azureAd = null)
    {
        if (authOptions.Mode == WebAuthMode.Dev && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"Auth:Mode 'Dev' is allowed only when the host environment is Development (current: '{environment.EnvironmentName}').");
        }

        if (!Uri.TryCreate(apiOptions.BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("TodoApi:BaseUrl must be set to the Todo API base URL (the APIM gateway URL in Azure).");
        }

        if (authOptions.Mode == WebAuthMode.Entra)
        {
            if (!Guid.TryParse(azureAd?["TenantId"], out _) || !Guid.TryParse(azureAd?["ClientId"], out _))
            {
                throw new InvalidOperationException("AzureAd:TenantId and AzureAd:ClientId (the todo-web application id) must be set when Auth:Mode is 'Entra'.");
            }

            if (string.IsNullOrWhiteSpace(apiOptions.Scope))
            {
                throw new InvalidOperationException("TodoApi:Scope must be set (api://{todo-api client id}/access_as_user) when Auth:Mode is 'Entra'.");
            }
        }
    }
}
