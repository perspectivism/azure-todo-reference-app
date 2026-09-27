using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Todo.Api;

namespace Todo.UnitTests.TestSupport;

internal sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "Todo.Api";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

internal static class TestHost
{
    public static IConfiguration Configuration(params (string Key, string? Value)[] settings)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    /// <summary>Builds the API's real authentication registration for the given configuration and environment.</summary>
    public static ServiceProvider BuildAuthServices(IConfiguration configuration, string environmentName = "Development", Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTodoApiAuthentication(configuration, new TestHostEnvironment(environmentName));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    public static DefaultHttpContext HttpContext(IServiceProvider services, params (string Name, string Value)[] headers)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        foreach (var (name, value) in headers)
        {
            context.Request.Headers[name] = value;
        }

        return context;
    }
}
