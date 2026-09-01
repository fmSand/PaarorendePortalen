using Microsoft.AspNetCore.Http;
using Parorendeportalen.Api.Middleware;

namespace Parorendeportalen.Api.Tests.Middleware;

public class SecFetchSiteMiddlewareTests
{
    private static async Task<(int StatusCode, bool NextCalled)> InvokeAsync(
        string method,
        string? secFetchSite
    )
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        if (secFetchSite is not null)
        {
            context.Request.Headers["Sec-Fetch-Site"] = secFetchSite;
        }

        var nextCalled = false;
        var sut = new SecFetchSiteMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await sut.InvokeAsync(context);

        return (context.Response.StatusCode, nextCalled);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task RejectsUnsafeMethod_FromSameSiteSiblingOrigin(string method)
    {
        var (statusCode, nextCalled) = await InvokeAsync(method, "same-site");

        Assert.Equal(StatusCodes.Status403Forbidden, statusCode);
        Assert.False(nextCalled);
    }

    [Theory]
    [InlineData("cross-site")]
    [InlineData("none")]
    public async Task RejectsUnsafeMethod_FromAnyNonSameOriginValue(string secFetchSite)
    {
        var (statusCode, nextCalled) = await InvokeAsync("POST", secFetchSite);

        Assert.Equal(StatusCodes.Status403Forbidden, statusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task AllowsUnsafeMethod_FromSameOrigin()
    {
        var (_, nextCalled) = await InvokeAsync("POST", "same-origin");

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task AllowsUnsafeMethod_WhenHeaderAbsent()
    {
        var (_, nextCalled) = await InvokeAsync("POST", secFetchSite: null);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task IgnoresSafeMethod_EvenFromCrossSite()
    {
        var (_, nextCalled) = await InvokeAsync("GET", "cross-site");

        Assert.True(nextCalled);
    }
}
