using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Parorendeportalen.Api.Authentication;

public static class OidcEvents
{
    public static OpenIdConnectEvents Create() =>
        new()
        {
            OnTokenValidated = OnTokenValidatedAsync,
            OnTicketReceived = KeepOnlySubInTheSession,
            OnRemoteFailure = OnRemoteFailureAsync,
        };

    private static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var validator = context.HttpContext.RequestServices.GetRequiredService<LoginValidator>();

        var result = await validator.ValidateAsync(
            context.Principal,
            context.HttpContext.RequestAborted
        );

        if (result.IsRejected)
        {
            context.Fail(result.FailureReason!);
        }
    }

    // Runs after OnTokenValidated, so LoginValidator has already read socialno and name
    private static Task KeepOnlySubInTheSession(TicketReceivedContext context)
    {
        var principal = context.Principal!;
        context.Principal = new ClaimsPrincipal(
            new ClaimsIdentity(principal.FindAll("sub"), principal.Identity?.AuthenticationType)
        );
        return Task.CompletedTask;
    }

    private static Task OnRemoteFailureAsync(RemoteFailureContext context)
    {
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";

        var detail =
            context.Failure?.Message is string message && LoginFailureReasons.All.Contains(message)
                ? message
                : "Login failed.";

        return context.Response.WriteAsJsonAsync(
            new
            {
                title = "Login failed.",
                detail,
                status = StatusCodes.Status403Forbidden,
            }
        );
    }
}
