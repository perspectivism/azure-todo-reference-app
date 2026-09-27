using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Todo.UnitTests.TestSupport;

/// <summary>Captures every log entry (formatted message, structured values, and exception text) for assertions.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<CapturedLog> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

    public void Dispose()
    {
    }

    /// <summary>All captured text, used to assert that sensitive values never reach the logs.</summary>
    public string AllText => string.Join('\n', Entries.Select(e => e.ToString()));

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
            entries.Enqueue(new CapturedLog(
                category,
                logLevel,
                formatter(state, exception),
                values.ToDictionary(v => v.Key, v => v.Value?.ToString(), StringComparer.Ordinal),
                exception?.ToString()));
        }
    }
}

internal sealed record CapturedLog(string Category, LogLevel Level, string Message, IReadOnlyDictionary<string, string?> Values, string? Exception)
{
    public override string ToString()
        => $"{Category} {Level} {Message} {string.Join(';', Values.Select(v => $"{v.Key}={v.Value}"))} {Exception}";
}
