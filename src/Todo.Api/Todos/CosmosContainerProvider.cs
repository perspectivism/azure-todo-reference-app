using Microsoft.Azure.Cosmos;

namespace Todo.Api.Todos;

/// <summary>Configuration section <c>Cosmos</c>.</summary>
public sealed class CosmosOptions
{
    public const string SectionName = "Cosmos";

    public const string EmulatorDocumentationUrl = "https://learn.microsoft.com/en-us/azure/cosmos-db/emulator";

    /// <summary>Local development only (Cosmos DB Emulator). Kept in untracked configuration, never committed.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Azure: account endpoint, accessed with the Function App's managed identity (key auth is disabled).</summary>
    public string? Endpoint { get; set; }

    public string DatabaseName { get; set; } = "todo";

    public string ContainerName { get; set; } = "todos";

    /// <summary>Create the database and container on first use. Local development only; Bicep owns them in Azure.</summary>
    public bool AutoCreate { get; set; }
}

/// <summary>Resolves the <c>todos</c> container, creating it first only when <see cref="CosmosOptions.AutoCreate"/> is true.</summary>
public sealed class CosmosContainerProvider(CosmosClient client, CosmosOptions options)
{
    public const string PartitionKeyPath = "/userId";

    private readonly Lazy<Task<Container>> _container = new(() => ResolveAsync(client, options));

    public async Task<Container> GetContainerAsync(CancellationToken cancellationToken)
    {
        // The shared initialization is not tied to a single request's cancellation.
        var task = _container.Value;
        return await task.WaitAsync(cancellationToken);
    }

    private static async Task<Container> ResolveAsync(CosmosClient client, CosmosOptions options)
    {
        if (!options.AutoCreate)
        {
            return client.GetContainer(options.DatabaseName, options.ContainerName);
        }

        Database database = await client.CreateDatabaseIfNotExistsAsync(options.DatabaseName);
        Container container = await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(options.ContainerName, PartitionKeyPath));
        return container;
    }
}
