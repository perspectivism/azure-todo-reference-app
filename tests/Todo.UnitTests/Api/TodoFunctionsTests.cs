using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Todo.Api.Auth;
using Todo.Api.Functions;
using Todo.Api.Todos;
using Todo.Contracts;

namespace Todo.UnitTests.Api;

public sealed class TodoFunctionsTests
{
    private const string UserA = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string UserB = "bbbbbbbb-0000-0000-0000-000000000002";

    private readonly TodoService _service = new(new InMemoryTodoRepository(), TimeProvider.System, NullLogger<TodoService>.Instance);
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private TodoFunctions FunctionsFor(string userId)
    {
        var currentUser = new CurrentUser();
        currentUser.Set(userId);
        return new TodoFunctions(_service, currentUser);
    }

    private static HttpRequest Request(string? body = null, string? query = null, long? contentLength = null)
    {
        var context = new DefaultHttpContext();
        if (body is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = contentLength ?? bytes.Length;
            context.Request.ContentType = "application/json";
        }

        if (query is not null)
        {
            context.Request.QueryString = new QueryString(query);
        }

        return context.Request;
    }

    private static int? StatusOf(IActionResult result) => result switch
    {
        ObjectResult o => o.StatusCode,
        StatusCodeResult s => s.StatusCode,
        _ => null,
    };

    [Fact]
    public async Task Spoofed_userId_in_body_and_query_is_ignored()
    {
        var body = $$"""{"title":"mine","userId":"{{UserB}}","id":"{{Guid.Empty}}","createdUtc":"2000-01-01T00:00:00Z"}""";

        var result = await FunctionsFor(UserA).Create(Request(body, query: $"?userId={UserB}"), _ct);

        var created = Assert.IsType<CreatedResult>(result);
        var todo = Assert.IsType<TodoResponse>(created.Value);
        Assert.NotEqual(Guid.Empty, todo.Id);
        Assert.NotEqual(2000, todo.CreatedUtc.Year);
        Assert.Equal($"/todos/{todo.Id}", created.Location);
        Assert.NotNull(await _service.GetAsync(UserA, todo.Id.ToString(), _ct));
        Assert.Null(await _service.GetAsync(UserB, todo.Id.ToString(), _ct));

        var listForB = await FunctionsFor(UserB).List(Request(query: $"?userId={UserA}"), _ct);
        var page = Assert.IsType<TodoListResponse>(Assert.IsType<OkObjectResult>(listForB).Value);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Body_over_16KB_returns_413()
    {
        var body = $$"""{"title":"{{new string('a', TodoLimits.MaxRequestBodyBytes)}}"}""";

        Assert.Equal(413, StatusOf(await FunctionsFor(UserA).Create(Request(body), _ct)));
    }

    [Fact]
    public async Task Body_over_16KB_without_content_length_returns_413()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(new string(' ', TodoLimits.MaxRequestBodyBytes + 10)));

        Assert.Equal(413, StatusOf(await FunctionsFor(UserA).Create(context.Request, _ct)));
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("""{"title":123}""")]
    [InlineData("""{"title":"t","isComplete":"yes"}""")]
    public async Task Malformed_body_returns_400(string body)
    {
        Assert.Equal(400, StatusOf(await FunctionsFor(UserA).Create(Request(body), _ct)));
    }

    [Theory]
    [InlineData(null, true, 50)]
    [InlineData("1", true, 1)]
    [InlineData("100", true, 100)]
    [InlineData("0", false, 0)]
    [InlineData("101", false, 0)]
    [InlineData("-1", false, 0)]
    [InlineData("abc", false, 0)]
    public void Limit_parsing(string? value, bool valid, int expected)
    {
        var values = value is null ? StringValues.Empty : new StringValues(value);

        Assert.Equal(valid, TodoFunctions.TryParseLimit(values, out var limit));
        if (valid)
        {
            Assert.Equal(expected, limit);
        }
    }
}
