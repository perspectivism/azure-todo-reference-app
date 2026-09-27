namespace Todo.Api.Todos;

/// <summary>The request body failed validation. Mapped to 400 with a validation ProblemDetails body.</summary>
public sealed class TodoValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("The todo request is invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>The paging continuation token is malformed or not accepted by the store. Mapped to 400.</summary>
public sealed class InvalidContinuationTokenException(Exception? innerException = null)
    : Exception("The continuation token is not valid.", innerException);
