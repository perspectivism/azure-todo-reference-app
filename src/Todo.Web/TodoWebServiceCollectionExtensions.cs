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
        ValidateStartup(authOptions, apiOptions, environment);

        services.AddSingleton(authOptions);
        services.AddCascadingAuthenticationState();
        services.AddAuthorization();

        switch (authOptions.Mode)
        {
            case WebAuthMode.Dev:
                services.AddAuthentication(DevWebAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, DevWebAuthenticationHandler>(DevWebAuthenticationHandler.SchemeName, _ => { });
                services.AddScoped<ITodoApiRequestAuthorizer, DevTodoApiRequestAuthorizer>();
                break;

            default:
                throw new InvalidOperationException($"Auth:Mode '{authOptions.Mode}' is not supported by this build.");
        }

        var baseUrl = apiOptions.BaseUrl!.TrimEnd('/') + "/";
        services.AddHttpClient<ITodoApiClient, TodoApiClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }

    internal static void ValidateStartup(WebAuthOptions authOptions, TodoApiOptions apiOptions, IHostEnvironment environment)
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
    }
}
