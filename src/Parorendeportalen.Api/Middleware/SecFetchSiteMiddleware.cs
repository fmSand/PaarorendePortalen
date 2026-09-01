namespace Parorendeportalen.Api.Middleware;

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
    public static IApplicationBuilder UseSecFetchSiteProtection(this IApplicationBuilder app) =>
        app.UseMiddleware<SecFetchSiteMiddleware>();
}
