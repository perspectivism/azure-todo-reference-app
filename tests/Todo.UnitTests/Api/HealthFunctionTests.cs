using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Todo.Api.Functions;
using Todo.Contracts;

namespace Todo.UnitTests.Api;

public sealed class HealthFunctionTests
{
    [Fact]
    public void Health_returns_200_with_status_only()
    {
        var result = new HealthFunction().Run(new DefaultHttpContext().Request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(new HealthResponse(HealthResponse.Healthy), ok.Value);
    }
}
