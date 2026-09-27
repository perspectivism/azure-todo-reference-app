namespace Todo.Contracts;

/// <summary>Server-side validation rules for <see cref="TodoRequest"/>. Titles are validated after trimming.</summary>
public static class TodoValidator
{
    public static IReadOnlyDictionary<string, string[]> Validate(TodoRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            errors["title"] = ["Title is required."];
        }
        else if (title.Length > TodoLimits.TitleMaxLength)
        {
            errors["title"] = [$"Title must be at most {TodoLimits.TitleMaxLength} characters."];
        }

        if (request.Description is { Length: > TodoLimits.DescriptionMaxLength })
        {
            errors["description"] = [$"Description must be at most {TodoLimits.DescriptionMaxLength} characters."];
        }

        return errors;
    }
}
