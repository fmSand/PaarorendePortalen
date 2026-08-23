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

    // Out-of-range values clamped
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<VisitResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<VisitResponse>>> Get(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var careRecipientId = await currentNextOfKin.GetCareRecipientIdAsync(cancellationToken);
        var result = await visitService.GetByCareRecipientIdAsync(
            careRecipientId, from, to, pageNumber, pageSize, cancellationToken);
        return Ok(result);
    }

    // 404 (BOLA)
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(VisitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VisitResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        var careRecipientId = await currentNextOfKin.GetCareRecipientIdAsync(cancellationToken);
        var visit = await visitService.GetByIdAsync(id, careRecipientId, cancellationToken);
        return visit is null ? NotFound() : Ok(visit);
    }
}
