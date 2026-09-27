using Todo.Contracts;

namespace Todo.UnitTests.Contracts;

public sealed class TodoValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Title_is_required(string? title)
    {
        var errors = TodoValidator.Validate(new TodoRequest { Title = title });

        Assert.Equal(["Title is required."], errors["title"]);
    }

    [Fact]
    public void Title_length_is_measured_after_trimming()
    {
        var padded = "  " + new string('a', TodoLimits.TitleMaxLength) + "  ";

        Assert.Empty(TodoValidator.Validate(new TodoRequest { Title = padded }));
    }

    [Fact]
    public void Title_longer_than_limit_is_rejected()
    {
        var errors = TodoValidator.Validate(new TodoRequest { Title = new string('a', TodoLimits.TitleMaxLength + 1) });

        Assert.Contains("title", errors.Keys);
    }

    [Fact]
    public void Description_is_optional_and_limited()
    {
        Assert.Empty(TodoValidator.Validate(new TodoRequest { Title = "t", Description = null }));
        Assert.Empty(TodoValidator.Validate(new TodoRequest { Title = "t", Description = new string('d', TodoLimits.DescriptionMaxLength) }));

        var errors = TodoValidator.Validate(new TodoRequest { Title = "t", Description = new string('d', TodoLimits.DescriptionMaxLength + 1) });
        Assert.Equal(["description"], errors.Keys);
    }
}
