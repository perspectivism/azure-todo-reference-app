using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.Worker;
using Todo.Api.Functions;

namespace Todo.UnitTests.Api;

/// <summary>
/// docs/openapi.yaml is hand-maintained and imported by API Management. These tests keep it in step with the
/// HTTP-triggered functions, so a route added, removed, or changed in code without updating the contract fails here
/// instead of breaking APIM routing after deployment.
/// </summary>
public sealed partial class OpenApiContractTests
{
    private static readonly string[] HttpMethods = ["get", "put", "post", "delete", "patch", "head", "options"];

    private static HashSet<string> CodeRoutes()
    {
        var routes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in typeof(HealthFunction).Assembly.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)))
        {
            if (method.GetCustomAttribute<FunctionAttribute>() is null)
            {
                continue;
            }

            foreach (var trigger in method.GetParameters().Select(p => p.GetCustomAttribute<HttpTriggerAttribute>()).OfType<HttpTriggerAttribute>())
            {
                foreach (var verb in trigger.Methods ?? [])
                {
                    routes.Add($"{verb.ToUpperInvariant()} /{trigger.Route}");
                }
            }
        }

        return routes;
    }

    /// <summary>
    /// Locates the repository from this source file's compile-time path (works when the build output is outside the
    /// repository), falling back to the test output folder (CI builds map source paths to "/_/").
    /// </summary>
    private static DirectoryInfo? RepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        foreach (var start in new[] { Path.GetDirectoryName(sourceFile), AppContext.BaseDirectory })
        {
            for (var dir = string.IsNullOrEmpty(start) ? null : new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "TodoApp.slnx")))
                {
                    return dir;
                }
            }
        }

        return null;
    }

    private static (HashSet<string> Routes, Dictionary<string, string> OperationIds) OpenApiRoutes()
    {
        var root = RepositoryRoot();
        if (root is null)
        {
            Assert.Fail("Could not locate the repository root (TodoApp.slnx) from the test source path.");
        }

        var lines = File.ReadAllLines(Path.Combine(root.FullName, "docs", "openapi.yaml"));

        var routes = new HashSet<string>(StringComparer.Ordinal);
        var operationIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var inPaths = false;
        string? path = null;
        string? current = null;
        foreach (var line in lines)
        {
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                inPaths = line.TrimEnd() == "paths:";
                continue;
            }

            if (!inPaths)
            {
                continue;
            }

            if (PathLine().Match(line) is { Success: true } p)
            {
                path = p.Groups[1].Value;
            }
            else if (MethodLine().Match(line) is { Success: true } m && path is not null && HttpMethods.Contains(m.Groups[1].Value))
            {
                current = $"{m.Groups[1].Value.ToUpperInvariant()} {path}";
                routes.Add(current);
            }
            else if (OperationIdLine().Match(line) is { Success: true } o && current is not null)
            {
                operationIds[current] = o.Groups[1].Value;
            }
        }

        return (routes, operationIds);
    }

    [Fact]
    public void Every_function_route_is_in_the_openapi_contract_and_vice_versa()
    {
        var code = CodeRoutes();
        var (contract, _) = OpenApiRoutes();

        Assert.NotEmpty(code);
        Assert.Equal(code.Order(StringComparer.Ordinal), contract.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_operation_has_a_unique_operation_id_and_health_is_getHealth()
    {
        var (contract, operationIds) = OpenApiRoutes();

        Assert.Equal(contract.Count, operationIds.Count);
        Assert.Equal(operationIds.Count, operationIds.Values.Distinct(StringComparer.Ordinal).Count());

        // infra/modules/apim.bicep attaches the anonymous (no validate-jwt) policy to the operation named getHealth.
        Assert.Equal("getHealth", operationIds["GET /health"]);
    }

    [GeneratedRegex(@"^  (/\S*):\s*$")]
    private static partial Regex PathLine();

    [GeneratedRegex(@"^    ([a-z]+):\s*$")]
    private static partial Regex MethodLine();

    [GeneratedRegex(@"^      operationId:\s*(\S+)\s*$")]
    private static partial Regex OperationIdLine();
}
