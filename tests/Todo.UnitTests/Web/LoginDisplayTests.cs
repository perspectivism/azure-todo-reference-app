using System.Security.Claims;
using Bunit;
using Todo.Web.Components.Layout;

namespace Todo.UnitTests.Web;

public sealed class LoginDisplayTests : BunitContext
{
    [Fact]
    public void Signed_in_user_sees_name_and_sign_in_name_and_sign_out()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("Ada Lovelace");
        auth.SetClaims(new Claim("name", "Ada Lovelace"), new Claim("preferred_username", "ada@example.com"));

        var cut = Render<LoginDisplay>();

        Assert.Equal("Ada Lovelace", cut.Find(".profile-name").TextContent);
        Assert.Equal("(ada@example.com)", cut.Find(".profile-sign-in-name").TextContent);
        Assert.Equal("authentication/sign-out", cut.Find("form").GetAttribute("action"));
    }

    [Fact]
    public void Anonymous_user_sees_sign_in()
    {
        AddAuthorization();

        var cut = Render<LoginDisplay>();

        Assert.Equal("authentication/sign-in", cut.Find("a").GetAttribute("href"));
        Assert.Empty(cut.FindAll(".profile"));
    }
}
