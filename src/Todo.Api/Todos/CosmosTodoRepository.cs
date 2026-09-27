using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;

namespace Todo.Api.Todos;

/// <summary>
/// Cosmos DB for NoSQL store. Container <c>todos</c> is partitioned by <c>/userId</c>. Point reads use id plus the
/// caller's partition key; every query is scoped to the caller's partition and filters on userId as well.
/// </summary>
public sealed class CosmosTodoRepository(CosmosContainerProvider containerProvider) : ITodoRepository
{
    public async Task<TodoItem?> GetAsync(string userId, string id, CancellationToken cancellationToken)
    {
        var container = await containerProvider.GetContainerAsync(cancellationToken);
        try
        {
            var response = await container.ReadItemAsync<TodoDocument>(id, new PartitionKey(userId), cancellationToken: cancellationToken);
            return response.Resource.UserId == userId ? response.Resource.ToItem() : null;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<TodoPage> ListAsync(string userId, int limit, string? continuationToken, CancellationToken cancellationToken)
    {
        var cosmosToken = continuationToken is null ? null : ContinuationTokens.Decode(continuationToken);
        var container = await containerProvider.GetContainerAsync(cancellationToken);

        var query = new QueryDefinition("SELECT * FROM c WHERE c.userId = @userId ORDER BY c.createdUtc DESC")
            .WithParameter("@userId", userId);
        var options = new QueryRequestOptions { PartitionKey = new PartitionKey(userId), MaxItemCount = limit };

        try
        {
            using var iterator = container.GetItemQueryIterator<TodoDocument>(query, cosmosToken, options);
            if (!iterator.HasMoreResults)
            {
                return new TodoPage([], null);
            }

            var response = await iterator.ReadNextAsync(cancellationToken);
            var items = response.Where(d => d.UserId == userId).Select(d => d.ToItem()).ToList();
            var next = string.IsNullOrEmpty(response.ContinuationToken) ? null : ContinuationTokens.Encode(response.ContinuationToken);
            return new TodoPage(items, next);
        }
        catch (CosmosException ex) when (cosmosToken is not null && ex.StatusCode == HttpStatusCode.BadRequest)
        {
            throw new InvalidContinuationTokenException(ex);
        }
    }

    public async Task CreateAsync(TodoItem item, CancellationToken cancellationToken)
    {
        var container = await containerProvider.GetContainerAsync(cancellationToken);
        await container.CreateItemAsync(TodoDocument.From(item), new PartitionKey(item.UserId), cancellationToken: cancellationToken);
    }

    public async Task<bool> ReplaceAsync(TodoItem item, CancellationToken cancellationToken)
    {
        var container = await containerProvider.GetContainerAsync(cancellationToken);
        try
        {
            await container.ReplaceItemAsync(TodoDocument.From(item), item.Id, new PartitionKey(item.UserId), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string userId, string id, CancellationToken cancellationToken)
    {
        var container = await containerProvider.GetContainerAsync(cancellationToken);
        try
        {
            using var response = await container.DeleteItemStreamAsync(id, new PartitionKey(userId), cancellationToken: cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }

            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>
    /// Stored document shape. Timestamps are fixed-width ISO 8601 UTC strings so that ORDER BY on createdUtc
    /// sorts chronologically.
    /// </summary>
    internal sealed record TodoDocument
    {
        private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("userId")]
        public required string UserId { get; init; }

        [JsonPropertyName("title")]
        public required string Title { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("isComplete")]
        public bool IsComplete { get; init; }

        [JsonPropertyName("createdUtc")]
        public required string CreatedUtc { get; init; }

        [JsonPropertyName("updatedUtc")]
        public required string UpdatedUtc { get; init; }

        public static TodoDocument From(TodoItem item) => new()
        {
            Id = item.Id,
            UserId = item.UserId,
            Title = item.Title,
            Description = item.Description,
            IsComplete = item.IsComplete,
            CreatedUtc = FormatTimestamp(item.CreatedUtc),
            UpdatedUtc = FormatTimestamp(item.UpdatedUtc),
        };

        public TodoItem ToItem() => new()
        {
            Id = Id,
            UserId = UserId,
            Title = Title,
            Description = Description,
            IsComplete = IsComplete,
            CreatedUtc = ParseTimestamp(CreatedUtc),
            UpdatedUtc = ParseTimestamp(UpdatedUtc),
        };

        internal static string FormatTimestamp(DateTimeOffset value)
            => value.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

        internal static DateTimeOffset ParseTimestamp(string value)
            => DateTimeOffset.ParseExact(value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }
}

/// <summary>Wraps Cosmos continuation tokens (JSON) as URL-safe base64 so clients can treat them as opaque strings.</summary>
internal static class ContinuationTokens
{
    public static string Encode(string cosmosToken)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(cosmosToken)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Decode(string token)
    {
        try
        {
            var base64 = token.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return decoded.Length > 0 ? decoded : throw new InvalidContinuationTokenException();
        }
        catch (FormatException ex)
        {
            throw new InvalidContinuationTokenException(ex);
        }
    }
}
