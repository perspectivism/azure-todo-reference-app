using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.UnitTests.TestSupport;
using Todo.Web.Api;
using Todo.Web.Components.Todos;

namespace Todo.UnitTests.Web;

public sealed class TodoBoardTests : BunitContext
{
    private readonly FakeTodoApiClient _api = new();

    public TodoBoardTests()
    {
        Services.AddSingleton<ITodoApiClient>(_api);
    }

    [Fact]
    public void Shows_loading_state_while_the_list_is_being_fetched()
    {
        var pending = new TaskCompletionSource<TodoListResponse>();
        _api.ListOverride = () => pending.Task;

        var cut = Render<TodoBoard>();

        Assert.NotNull(cut.Find(".loading"));
        Assert.Empty(cut.FindAll(".todo-item"));
    }

    [Fact]
    public void Shows_empty_state_when_the_user_has_no_todos()
    {
        var cut = Render<TodoBoard>();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".empty-state")));
    }

    [Fact]
    public void Shows_error_state_without_internal_details_and_can_retry()
    {
        _api.NextFailure = new HttpRequestException("connection refused to internal-host:1234");
        _api.Add("Recovered");

        var cut = Render<TodoBoard>();

        var error = cut.WaitForElement(".load-error");
        Assert.DoesNotContain("internal-host", error.TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".todo-item"));

        cut.Find(".retry").Click();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".todo-item")));
        Assert.Equal(2, _api.ListCalls);
    }

    [Fact]
    public void Shows_the_list_with_completed_items_marked()
    {
        _api.Add("Open task");
        _api.Add("Done task", isComplete: true, description: "details");

        var cut = Render<TodoBoard>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".todo-item").Count));
        var titles = cut.FindAll(".todo-title");
        Assert.Equal("Done task", titles[0].TextContent);
        Assert.Contains("text-decoration-line-through", titles[0].ClassName, StringComparison.Ordinal);
        Assert.Equal("details", cut.Find(".todo-description").TextContent);
    }

    [Fact]
    public void Empty_title_shows_validation_message_and_does_not_call_the_api()
    {
        var cut = Render<TodoBoard>();
        cut.WaitForElement(".empty-state");

        cut.Find("#new-title").Change("   ");
        cut.Find("form.new-todo").Submit();

        Assert.Contains("Title is required.", cut.Find(".validation-message").TextContent, StringComparison.Ordinal);
        Assert.Empty(_api.Created);
    }

    [Fact]
    public void Creating_a_todo_adds_it_to_the_list_and_clears_the_form()
    {
        var cut = Render<TodoBoard>();
        cut.WaitForElement(".empty-state");

        cut.Find("#new-title").Change("Write tests");
        cut.Find("#new-description").Change("with bUnit");
        cut.Find("form.new-todo").Submit();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".todo-item")));
        Assert.Equal("Write tests", Assert.Single(_api.Created).Title);
        Assert.Equal("Write tests", cut.Find(".todo-title").TextContent);
        Assert.Equal(string.Empty, cut.Find("#new-title").GetAttribute("value") ?? string.Empty);
    }

    [Fact]
    public void Server_validation_errors_are_shown_on_create()
    {
        var cut = Render<TodoBoard>();
        cut.WaitForElement(".empty-state");
        _api.NextFailure = new TodoApiException(400, new Dictionary<string, string[]> { ["title"] = ["Title must be at most 200 characters."] });

        cut.Find("#new-title").Change("valid locally");
        cut.Find("form.new-todo").Submit();

        cut.WaitForAssertion(() => Assert.Contains("at most 200", cut.Find(".server-errors").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Toggling_completes_and_uncompletes_through_the_api()
    {
        var todo = _api.Add("Toggle me");
        var cut = Render<TodoBoard>();
        cut.WaitForElement(".todo-item");

        cut.Find(".toggle").Change(true);
        cut.WaitForAssertion(() => Assert.Contains("text-decoration-line-through", cut.Find(".todo-title").ClassName, StringComparison.Ordinal));
        cut.Find(".toggle").Change(false);

        cut.WaitForAssertion(() => Assert.Equal(2, _api.Updated.Count));
        Assert.All(_api.Updated, u => Assert.Equal(todo.Id, u.Id));
        Assert.True(_api.Updated[0].Request.IsComplete);
        Assert.False(_api.Updated[1].Request.IsComplete);
        Assert.Equal("Toggle me", _api.Updated[0].Request.Title);
    }

    [Fact]
    public void Editing_saves_the_new_title()
    {
        var todo = _api.Add("Old title");
        var cut = Render<TodoBoard>();
        cut.WaitForElement(".todo-item");

        cut.Find(".edit").Click();
        cut.Find(".edit-title").Change("New title");
        cut.Find("form.edit-todo").Submit();

        cut.WaitForAssertion(() => Assert.Equal("New title", cut.Find(".todo-title").TextContent));
        Assert.Equal((todo.Id, "New title"), (_api.Updated.Single().Id, _api.Updated.Single().Request.Title));
    }

    [Fact]
    public void Editing_to_an_empty_title_shows_validation_message()
    {
        _api.Add("Keep me");
        var cut = Render<TodoBoard>();
        cut.WaitForElement(".todo-item");

        cut.Find(".edit").Click();
        cut.Find(".edit-title").Change("");
        cut.Find("form.edit-todo").Submit();

        Assert.Contains("Title is required.", cut.Find(".validation-message").TextContent, StringComparison.Ordinal);
        Assert.Empty(_api.Updated);
    }

    [Fact]
    public void Deleting_removes_the_item()
    {
        var todo = _api.Add("Delete me");
        var cut = Render<TodoBoard>();
        cut.WaitForElement(".todo-item");

        cut.Find(".delete").Click();

        cut.WaitForElement(".empty-state");
        Assert.Equal(todo.Id, Assert.Single(_api.Deleted));
    }

    [Fact]
    public void Failed_action_keeps_the_list_and_shows_a_message()
    {
        _api.Add("Stays visible");
        var cut = Render<TodoBoard>();
        cut.WaitForElement(".todo-item");
        _api.NextFailure = new TodoApiException(500);

        cut.Find(".delete").Click();

        cut.WaitForElement(".action-error");
        Assert.Single(cut.FindAll(".todo-item"));
    }
}
