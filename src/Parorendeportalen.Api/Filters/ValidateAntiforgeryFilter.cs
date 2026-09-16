using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Parorendeportalen.Api.Filters;

// MVC ships AutoValidateAntiforgeryTokenAttribute, but it resolves a filter only
// AddControllersWithViews registers, and this API has no views. Same rule over the
// same methods, answering in the problem-details shape the rest of the API uses.
public sealed class ValidateAntiforgeryFilter(
    IAntiforgery antiforgery,
    ILogger<ValidateAntiforgeryFilter> logger
) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var method = context.HttpContext.Request.Method;
        if (
            HttpMethods.IsGet(method)
            || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method)
            || HttpMethods.IsTrace(method)
        )
        {
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException exception)
        {
            logger.LogWarning(
                exception,
                "Antiforgery validation failed for {Method} {Path}.",
                method,
                context.HttpContext.Request.Path
            );

            context.Result = new ObjectResult(
                new ProblemDetails
                {
                    Title = "Missing or invalid antiforgery token.",
                    Detail =
                        "Fetch a token from /api/antiforgery/token and send it as X-XSRF-TOKEN.",
                    Status = StatusCodes.Status400BadRequest,
                }
            )
            {
                StatusCode = StatusCodes.Status400BadRequest,
            };
        }
    }
}
