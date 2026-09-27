using System.ComponentModel.DataAnnotations;

namespace Todo.Contracts;

/// <summary>
/// Body of <c>POST /todos</c> and <c>PUT /todos/{id}</c>. Only these three fields are read;
/// any other supplied field (including <c>userId</c>, <c>id</c>, and timestamps) is ignored.
/// </summary>
public sealed class TodoRequest
{
    [Required(ErrorMessage = "Title is required.")]
    [StringLength(TodoLimits.TitleMaxLength, ErrorMessage = "Title must be at most {1} characters.")]
    public string? Title { get; set; }

    [StringLength(TodoLimits.DescriptionMaxLength, ErrorMessage = "Description must be at most {1} characters.")]
    public string? Description { get; set; }

    public bool IsComplete { get; set; }
}
