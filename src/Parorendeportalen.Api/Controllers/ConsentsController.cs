using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Services.Access;
using Parorendeportalen.Api.Services.Kinship;

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
        [FromQuery, BindRequired] int careRecipientId,
        CancellationToken cancellationToken
    )
    {
        var current = await currentNextOfKin.GetCurrentAsync(cancellationToken);
        if (current is null || !current.CareRecipientIds.Contains(careRecipientId))
        {
            return NotFound();
        }

        var categories = await consentService.GetConsentedCategoriesAsync(
            current.NextOfKinId,
            careRecipientId,
            cancellationToken
        );

        return Ok(categories);
    }
}
