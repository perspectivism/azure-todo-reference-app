namespace Todo.Api.Todos;

/// <summary>
/// Creates a value once and caches it, but only on success: if creation fails (for example the Cosmos DB Emulator
/// is not running yet), the next caller tries again instead of receiving the cached failure until a restart.
/// Concurrent callers share a single creation attempt.
/// </summary>
internal sealed class RetryingAsyncLazy<T>(Func<CancellationToken, Task<T>> factory)
    where T : class
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private T? _value;

    public async Task<T> GetValueAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _value) is { } value)
        {
            return value;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_value is { } cached)
            {
                return cached;
            }

            var created = await factory(cancellationToken);
            Volatile.Write(ref _value, created);
            return created;
        }
        finally
        {
            _gate.Release();
        }
    }
}
