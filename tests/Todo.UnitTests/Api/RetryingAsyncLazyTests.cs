using Todo.Api.Todos;

namespace Todo.UnitTests.Api;

public sealed class RetryingAsyncLazyTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Successful_value_is_created_once_and_cached()
    {
        var calls = 0;
        var lazy = new RetryingAsyncLazy<object>(_ =>
        {
            calls++;
            return Task.FromResult(new object());
        });

        var first = await lazy.GetValueAsync(_ct);
        var second = await lazy.GetValueAsync(_ct);

        Assert.Same(first, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Failure_is_not_cached_and_the_next_call_retries()
    {
        var calls = 0;
        var lazy = new RetryingAsyncLazy<string>(_ =>
        {
            calls++;
            return calls == 1
                ? Task.FromException<string>(new HttpRequestException("emulator not reachable"))
                : Task.FromResult("container");
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => lazy.GetValueAsync(_ct));
        Assert.Equal("container", await lazy.GetValueAsync(_ct));
        Assert.Equal("container", await lazy.GetValueAsync(_ct));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Concurrent_callers_share_one_creation()
    {
        var calls = 0;
        var release = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lazy = new RetryingAsyncLazy<object>(_ =>
        {
            Interlocked.Increment(ref calls);
            return release.Task;
        });

        var waiters = Enumerable.Range(0, 10).Select(_ => lazy.GetValueAsync(_ct)).ToArray();
        release.SetResult(new object());
        var values = await Task.WhenAll(waiters);

        Assert.Equal(1, calls);
        Assert.All(values, v => Assert.Same(values[0], v));
    }
}
