using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Parorendeportalen.Api.Dtos.Planning;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Models.Access;
using Parorendeportalen.Api.Services.Access;
using Parorendeportalen.Api.Services.Planning;

namespace Parorendeportalen.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class VedtakController(
    IVedtakService vedtakService,
    IHealthDataAccessPolicy accessPolicy
) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<VedtakResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<VedtakResponse>>> Get(
        [FromQuery] int? careRecipientId,
        CancellationToken cancellationToken
    )
    {
        if (careRecipientId is null)
        {
            ModelState.AddModelError(nameof(careRecipientId), "careRecipientId is required.");
            return ValidationProblem(ModelState);
        }

        var access = await accessPolicy.AuthorizeReadAsync(
            careRecipientId.Value,
            DataCategory.Vedtak,
            cancellationToken
        );
        if (access is not AccessDecision.Granted)
        {
            return this.Denied(access);
        }

        var vedtak = await vedtakService.GetByCareRecipientIdAsync(
            careRecipientId.Value,
            cancellationToken
        );
        return Ok(vedtak);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(VedtakResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VedtakResponse>> GetById(
        int id,
        [FromQuery] int? careRecipientId,
        CancellationToken cancellationToken
    )
    {
        if (careRecipientId is null)
        {
            ModelState.AddModelError(nameof(careRecipientId), "careRecipientId is required.");
            return ValidationProblem(ModelState);
        }

        var access = await accessPolicy.AuthorizeReadAsync(
            careRecipientId.Value,
            DataCategory.Vedtak,
            cancellationToken
        );
        if (access is not AccessDecision.Granted)
        {
            return this.Denied(access);
        }

        var vedtak = await vedtakService.GetByIdAsync(id, careRecipientId.Value, cancellationToken);
        return vedtak is null ? NotFound() : Ok(vedtak);
    }
}
