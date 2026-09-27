using Microsoft.Extensions.DependencyInjection;
using Todo.Api;
using Todo.Api.Todos;
using Todo.UnitTests.TestSupport;

namespace Todo.UnitTests.Api;

public sealed class CosmosStorageTests
{
    [Fact]
    public void Missing_cosmos_configuration_fails_fast_with_emulator_guidance()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddTodoStorage(TestHost.Configuration(), new TestHostEnvironment("Development")));

        Assert.Contains(CosmosOptions.EmulatorDocumentationUrl, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoCreate_is_rejected_outside_Development()
    {
        var options = new CosmosOptions { Endpoint = "https://example.documents.azure.com:443/", AutoCreate = true };

        Assert.Throws<InvalidOperationException>(() =>
            TodoApiServiceCollectionExtensions.ValidateCosmosOptions(options, new TestHostEnvironment("Production")));
    }

    [Fact]
    public void Endpoint_without_key_is_accepted_for_managed_identity()
    {
        var options = new CosmosOptions { Endpoint = "https://example.documents.azure.com:443/" };

        TodoApiServiceCollectionExtensions.ValidateCosmosOptions(options, new TestHostEnvironment("Production"));
    }

    [Fact]
    public void Continuation_tokens_round_trip_as_url_safe_strings()
    {
        const string cosmosToken = """[{"token":"+RID:~abc/def==#RT:1#TRC:2","range":{"min":"","max":"FF"}}]""";

        var encoded = ContinuationTokens.Encode(cosmosToken);

        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.DoesNotContain('=', encoded);
        Assert.Equal(cosmosToken, ContinuationTokens.Decode(encoded));
    }

    [Theory]
    [InlineData("%%%")]
    [InlineData("a")]
    [InlineData("")]
    public void Invalid_continuation_tokens_are_rejected(string token)
    {
        Assert.Throws<InvalidContinuationTokenException>(() => ContinuationTokens.Decode(token));
    }

    [Fact]
    public void Stored_timestamps_are_fixed_width_and_sort_chronologically()
    {
        var whole = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var later = whole.AddTicks(5_000_000);
        var offset = new DateTimeOffset(2026, 1, 1, 14, 0, 0, TimeSpan.FromHours(2));

        var a = CosmosTodoRepository.TodoDocument.FormatTimestamp(whole);
        var b = CosmosTodoRepository.TodoDocument.FormatTimestamp(later);

        Assert.Equal("2026-01-01T12:00:00.0000000Z", a);
        Assert.Equal(a.Length, b.Length);
        Assert.True(string.CompareOrdinal(a, b) < 0);
        Assert.Equal(a, CosmosTodoRepository.TodoDocument.FormatTimestamp(offset));
        Assert.Equal(later, CosmosTodoRepository.TodoDocument.ParseTimestamp(b));
    }
}
