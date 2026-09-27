namespace Todo.Contracts;

/// <summary>Validation limits shared by the API and the Blazor UI.</summary>
public static class TodoLimits
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int MaxRequestBodyBytes = 16 * 1024;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
}
