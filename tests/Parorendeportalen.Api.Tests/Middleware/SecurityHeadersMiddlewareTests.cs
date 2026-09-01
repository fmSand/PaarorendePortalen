using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Parorendeportalen.Api.Middleware;

namespace Parorendeportalen.Api.Tests.Middleware;

public class SecurityHeadersMiddlewareTests
{
    private const string ExpectedContentSecurityPolicy =
        "default-src 'self'; connect-src 'self'; frame-ancestors 'none'";

    private sealed class CapturingResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];

        public override void OnStarting(Func<object, Task> callback, object state) =>
            _onStarting.Add((callback, state));

        public async Task StartResponseAsync()
        {
            foreach (var (callback, state) in _onStarting)
            {
                await callback(state);
            }
        }
    }

    private static (DefaultHttpContext Context, CapturingResponseFeature Response) CreateContext()
    {
        var responseFeature = new CapturingResponseFeature();
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new HttpRequestFeature());
        features.Set<IHttpResponseFeature>(responseFeature);

        return (new DefaultHttpContext(features), responseFeature);
    }

    private static async Task<IHeaderDictionary> InvokeAndStartResponseAsync(
        RequestDelegate? next = null
    )
    {
        var (context, responseFeature) = CreateContext();
        var sut = new SecurityHeadersMiddleware(next ?? (_ => Task.CompletedTask));

        await sut.InvokeAsync(context);
        await responseFeature.StartResponseAsync();

        return context.Response.Headers;
    }

    [Theory]
    [InlineData("X-Content-Type-Options", "nosniff")]
    [InlineData("X-Frame-Options", "DENY")]
    [InlineData("Referrer-Policy", "strict-origin-when-cross-origin")]
    [InlineData("Content-Security-Policy", ExpectedContentSecurityPolicy)]
    public async Task InvokeAsync_OnResponseStarting_SetsSecurityHeaderToExactValue(
        string headerName,
        string expectedValue
    )
    {
        var headers = await InvokeAndStartResponseAsync();

        Assert.Equal(expectedValue, headers[headerName]);
        Assert.Single(headers[headerName]!);
    }

    [Fact]
    public async Task InvokeAsync_OnResponseStarting_SetsAllFourSecurityHeaders()
    {
        var headers = await InvokeAndStartResponseAsync();

        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", headers["X-Frame-Options"]);
        Assert.Equal("strict-origin-when-cross-origin", headers["Referrer-Policy"]);
        Assert.Equal(ExpectedContentSecurityPolicy, headers["Content-Security-Policy"]);
    }

    [Fact]
    public async Task InvokeAsync_BeforeResponseStarts_DoesNotSetHeadersYet()
    {
        var (context, _) = CreateContext();
        var sut = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

        await sut.InvokeAsync(context);

        Assert.False(context.Response.Headers.ContainsKey("X-Content-Type-Options"));
        Assert.False(context.Response.Headers.ContainsKey("X-Frame-Options"));
        Assert.False(context.Response.Headers.ContainsKey("Referrer-Policy"));
        Assert.False(context.Response.Headers.ContainsKey("Content-Security-Policy"));
    }

    [Fact]
    public async Task InvokeAsync_AnyRequest_CallsNextMiddleware()
    {
        var nextCalled = false;

        await InvokeAndStartResponseAsync(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_WhenDownstreamReturnsErrorStatus_StillSetsSecurityHeaders()
    {
        var headers = await InvokeAndStartResponseAsync(context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        });

        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal(ExpectedContentSecurityPolicy, headers["Content-Security-Policy"]);
    }

    [Fact]
    public async Task InvokeAsync_WhenDownstreamAlreadySetFrameOptions_OverwritesWithDeny()
    {
        var headers = await InvokeAndStartResponseAsync(context =>
        {
            context.Response.Headers["X-Frame-Options"] = "ALLOWALL";
            return Task.CompletedTask;
        });

        Assert.Equal("DENY", headers["X-Frame-Options"]);
        Assert.Single(headers["X-Frame-Options"]!);
    }

    [Fact]
    public async Task InvokeAsync_ContentSecurityPolicy_ForbidsFramingAndRestrictsDefaultAndConnectSourcesToSelf()
    {
        var headers = await InvokeAndStartResponseAsync();

        var policy = headers["Content-Security-Policy"].ToString();
        Assert.Contains("default-src 'self'", policy);
        Assert.Contains("connect-src 'self'", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
    }
}
