using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Parorendeportalen.Api.Dtos.Planning;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Models.Access;
using Parorendeportalen.Api.Services;
using Parorendeportalen.Api.Services.Access;
using Parorendeportalen.Api.Services.Planning;

namespace Parorendeportalen.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class DayPlanController(
    IDayPlanService dayPlanService,
    IHealthDataAccessPolicy accessPolicy,
    TimeProvider timeProvider
) : ControllerBase
{
    private static readonly DataCategory[] Required = [DataCategory.Vedtak, DataCategory.Visits];

    [HttpGet]
    [ProducesResponseType(typeof(DayPlanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DayPlanResponse>> Get(
        [FromQuery] int? careRecipientId,
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken
    )
    {
        if (careRecipientId is null)
        {
            ModelState.AddModelError(nameof(careRecipientId), "careRecipientId is required.");
            return ValidationProblem(ModelState);
        }

        foreach (var category in Required)
        {
            var access = await accessPolicy.AuthorizeReadAsync(
                careRecipientId.Value,
                category,
                cancellationToken
            );
            if (access is not AccessDecision.Granted)
            {
                return this.Denied(access);
            }
        }

        // Today in Norway: near midnight the two dates differ and the plan would open on yesterday.
        var requested = date ?? NorwegianTime.DateOf(timeProvider.GetUtcNow());

        var plan = await dayPlanService.GetAsync(
            careRecipientId.Value,
            requested,
            cancellationToken
        );
        return Ok(plan);
    }
}
