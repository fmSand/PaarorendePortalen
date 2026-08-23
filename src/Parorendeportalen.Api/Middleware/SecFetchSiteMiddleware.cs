namespace Parorendeportalen.Api.Middleware;

// CSRF layer 2 of 3 (ADR-0004). Only same-origin requests may use an unsafe
// method.
public sealed class SecFetchSiteMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> UnsafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete,
    };

    public async Task InvokeAsync(HttpContext context)
    {
        var isUnsafeMethod = UnsafeMethods.Contains(context.Request.Method);

        // Same-site is rejected too, not just cross-site - a SameSite=Lax cookie still rides same-site requests
        // No header means a non-browser client; layers 1 and 3 cover that case instead
        var hasHeader = context.Request.Headers.TryGetValue("Sec-Fetch-Site", out var secFetchSite);
        var isDisallowedOrigin = hasHeader && secFetchSite.ToString() != "same-origin";

        if (isUnsafeMethod && isDisallowedOrigin)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }
}

public static class SecFetchSiteMiddlewareExtensions
{
    public static IApplicationBuilder UseSecFetchSiteProtection(this IApplicationBuilder app)
        => app.UseMiddleware<SecFetchSiteMiddleware>();
}
