using Microsoft.AspNetCore.Mvc;
using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Controllers;

public static class AccessDecisionResults
{
    public static ActionResult Denied(this ControllerBase controller, AccessDecision decision)
    {
        ArgumentNullException.ThrowIfNull(controller);

        return decision == AccessDecision.DeniedNoConsent
            ? controller.Problem(
                title: "No consent for this information.",
                detail: "The care recipient has not shared this category with you.",
                statusCode: StatusCodes.Status403Forbidden
            )
            : controller.NotFound();
    }
}
