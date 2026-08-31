using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class VisitsController(IVisitService visitService, ICurrentNextOfKinAccessor currentNextOfKin) : ControllerBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    // careRecipientId is required - a caller may hold several grants, and picking
    // one for them would make the response depend on how many they have.
    // Out-of-range paging values clamped.
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<VisitResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<VisitResponse>>> Get(
        [FromQuery] int? careRecipientId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (careRecipientId is null)
        {
            ModelState.AddModelError(nameof(careRecipientId), "careRecipientId is required.");
            return ValidationProblem(ModelState);
        }

        if (!await currentNextOfKin.HasAccessToAsync(careRecipientId.Value, cancellationToken))
        {
            return NotFound();
        }

        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var result = await visitService.GetByCareRecipientIdAsync(
            careRecipientId.Value, from, to, pageNumber, pageSize, cancellationToken);
        return Ok(result);
    }

    // 404 (BOLA)
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(VisitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VisitResponse>> GetById(
        int id, [FromQuery] int? careRecipientId, CancellationToken cancellationToken)
    {
        if (careRecipientId is null)
        {
            ModelState.AddModelError(nameof(careRecipientId), "careRecipientId is required.");
            return ValidationProblem(ModelState);
        }

        if (!await currentNextOfKin.HasAccessToAsync(careRecipientId.Value, cancellationToken))
        {
            return NotFound();
        }

        var visit = await visitService.GetByIdAsync(id, careRecipientId.Value, cancellationToken);
        return visit is null ? NotFound() : Ok(visit);
    }
}
