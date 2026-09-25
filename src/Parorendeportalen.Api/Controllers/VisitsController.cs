using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Dtos.Visits;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Models.Access;
using Parorendeportalen.Api.Services;
using Parorendeportalen.Api.Services.Access;
using Parorendeportalen.Api.Services.Visits;

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

    // Out-of-range paging values clamped.
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<VisitResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<VisitResponse>>> Get(
        [FromQuery, BindRequired] int careRecipientId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default
    )
    {
        var access = await accessPolicy.AuthorizeReadAsync(
            careRecipientId,
            DataCategory.Visits,
            cancellationToken
        );
        if (access is not AccessDecision.Granted)
        {
            return this.Denied(access);
        }

        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var result = await visitService.GetByCareRecipientIdAsync(
            careRecipientId,
            from?.ToUniversalTime(),
            to?.ToUniversalTime(),
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
        [FromQuery, BindRequired] int careRecipientId,
        CancellationToken cancellationToken
    )
    {
        var access = await accessPolicy.AuthorizeReadAsync(
            careRecipientId,
            DataCategory.Visits,
            cancellationToken
        );
        if (access is not AccessDecision.Granted)
        {
            return this.Denied(access);
        }

        var visit = await visitService.GetByIdAsync(id, careRecipientId, cancellationToken);
        if (visit is null)
        {
            return NotFound();
        }

        this.SetETag(visit.Version);
        return Ok(visit);
    }

    [HttpPost]
    [ProducesResponseType(typeof(VisitResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VisitResponse>> Create(
        [FromBody] CreateVisitRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var access = await accessPolicy.AuthorizeWriteAsync(
            request.CareRecipientId,
            DataCategory.Visits,
            cancellationToken
        );
        if (access is not AccessDecision.Granted)
        {
            return this.Denied(access);
        }

        var created = await visitService.CreateAsync(request, cancellationToken);

        this.SetETag(created.Version);
        return CreatedAtAction(
            nameof(GetById),
            new { id = created.Id, careRecipientId = created.CareRecipientId },
            created
        );
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(VisitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<VisitResponse>> Update(
        int id,
        [FromQuery, BindRequired] int careRecipientId,
        [FromBody] UpdateVisitRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var access = await accessPolicy.AuthorizeWriteAsync(
            careRecipientId,
            DataCategory.Visits,
            cancellationToken
        );
        if (access is not AccessDecision.Granted)
        {
            return this.Denied(access);
        }

        var precondition = this.ReadExpectedVersion(out var expectedVersion);
        if (precondition is not null)
        {
            return precondition;
        }

        var result = await visitService.UpdateAsync(
            id,
            careRecipientId,
            request,
            expectedVersion,
            cancellationToken
        );
        if (result.Outcome is not WriteOutcome.Succeeded)
        {
            return this.Refused(result.Outcome);
        }

        this.SetETag(result.Value!.Version);
        return Ok(result.Value);
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult> Delete(
        int id,
        [FromQuery, BindRequired] int careRecipientId,
        CancellationToken cancellationToken
    )
    {
        var access = await accessPolicy.AuthorizeWriteAsync(
            careRecipientId,
            DataCategory.Visits,
            cancellationToken
        );
        if (access is not AccessDecision.Granted)
        {
            return this.Denied(access);
        }

        var precondition = this.ReadExpectedVersion(out var expectedVersion);
        if (precondition is not null)
        {
            return precondition;
        }

        var outcome = await visitService.DeleteAsync(
            id,
            careRecipientId,
            expectedVersion,
            cancellationToken
        );

        return outcome is WriteOutcome.Succeeded ? NoContent() : this.Refused(outcome);
    }
}
