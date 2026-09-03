using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class ConsentsController(
    IConsentService consentService,
    ICurrentNextOfKinAccessor currentNextOfKin
) : ControllerBase
{
    // Reports on access, so it is not a health-data read and is not logged.
    // 404 for an ungranted id, same posture as the visit endpoints.
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<DataCategory>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<DataCategory>>> Get(
        [FromQuery] int? careRecipientId,
        CancellationToken cancellationToken
    )
    {
        if (careRecipientId is null)
        {
            ModelState.AddModelError(nameof(careRecipientId), "careRecipientId is required.");
            return ValidationProblem(ModelState);
        }

        var current = await currentNextOfKin.GetCurrentAsync(cancellationToken);
        if (current is null || !current.CareRecipientIds.Contains(careRecipientId.Value))
        {
            return NotFound();
        }

        var categories = await consentService.GetConsentedCategoriesAsync(
            current.NextOfKinId,
            careRecipientId.Value,
            cancellationToken
        );

        return Ok(categories);
    }
}
