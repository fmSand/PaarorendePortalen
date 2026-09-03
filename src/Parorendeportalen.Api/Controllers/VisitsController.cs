using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class VisitsController(
    IVisitService visitService,
    IHealthDataAccessPolicy accessPolicy
) : ControllerBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    // careRecipientId is required - a caller may hold several grants, and picking
    // one for them would make the response depend on how many they have.
    // Out-of-range paging values clamped.
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<VisitResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<VisitResponse>>> Get(
        [FromQuery] int? careRecipientId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default
    )
    {
        if (careRecipientId is null)
        {
            ModelState.AddModelError(nameof(careRecipientId), "careRecipientId is required.");
            return ValidationProblem(ModelState);
        }

        var access = await accessPolicy.AuthorizeReadAsync(
            careRecipientId.Value,
            DataCategory.Visits,
            cancellationToken
        );
        if (access is not AccessDecision.Granted)
        {
            return Denied(access);
        }

        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var result = await visitService.GetByCareRecipientIdAsync(
            careRecipientId.Value,
            from,
            to,
            pageNumber,
            pageSize,
            cancellationToken
        );
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(VisitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VisitResponse>> GetById(
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
            DataCategory.Visits,
            cancellationToken
        );
        if (access is not AccessDecision.Granted)
        {
            return Denied(access);
        }

        var visit = await visitService.GetByIdAsync(id, careRecipientId.Value, cancellationToken);
        return visit is null ? NotFound() : Ok(visit);
    }

    // No kinship looks like a missing care recipient (404, BOLA). No consent is
    // a 403: the caller holds a grant, so this person's existence is not news.
    private ActionResult Denied(AccessDecision decision) =>
        decision == AccessDecision.DeniedNoConsent
            ? Problem(
                title: "No consent for this information.",
                detail: "The care recipient has not shared this category with you.",
                statusCode: StatusCodes.Status403Forbidden
            )
            : NotFound();
}
