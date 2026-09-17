using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Parorendeportalen.Api.Dtos.Visits;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Models.Access;
using Parorendeportalen.Api.Services;
using Parorendeportalen.Api.Services.Access;
using Parorendeportalen.Api.Services.Visits;

namespace Parorendeportalen.Api.Controllers;

[ApiController]
[Route("api/visits/{visitId:int}/comments")]
[Authorize]
public sealed class VisitCommentsController(
    IVisitCommentService commentService,
    IHealthDataAccessPolicy accessPolicy
) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<VisitCommentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<VisitCommentResponse>>> Get(
        int visitId,
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
            return this.Denied(access);
        }

        var thread = await commentService.GetByVisitIdAsync(
            visitId,
            careRecipientId.Value,
            cancellationToken
        );

        return thread is null ? NotFound() : Ok(thread);
    }

    [HttpPost]
    [ProducesResponseType(typeof(VisitCommentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VisitCommentResponse>> Create(
        int visitId,
        [FromQuery] int? careRecipientId,
        [FromBody] CreateVisitCommentRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        if (careRecipientId is null)
        {
            ModelState.AddModelError(nameof(careRecipientId), "careRecipientId is required.");
            return ValidationProblem(ModelState);
        }

        var access = await accessPolicy.AuthorizeWriteAsync(
            careRecipientId.Value,
            DataCategory.Visits,
            cancellationToken
        );
        if (access is not AccessDecision.Granted)
        {
            return this.Denied(access);
        }

        var result = await commentService.CreateAsync(
            visitId,
            careRecipientId.Value,
            request,
            cancellationToken
        );
        if (result.Outcome is not WriteOutcome.Succeeded)
        {
            return this.Refused(result.Outcome);
        }

        this.SetETag(result.Value!.Version);
        return CreatedAtAction(
            nameof(Get),
            new { visitId, careRecipientId = careRecipientId.Value },
            result.Value
        );
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(VisitCommentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<VisitCommentResponse>> Update(
        int visitId,
        int id,
        [FromQuery] int? careRecipientId,
        [FromBody] UpdateVisitCommentRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        if (careRecipientId is null)
        {
            ModelState.AddModelError(nameof(careRecipientId), "careRecipientId is required.");
            return ValidationProblem(ModelState);
        }

        var access = await accessPolicy.AuthorizeWriteAsync(
            careRecipientId.Value,
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

        var result = await commentService.UpdateAsync(
            id,
            visitId,
            careRecipientId.Value,
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
        int visitId,
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

        var access = await accessPolicy.AuthorizeWriteAsync(
            careRecipientId.Value,
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

        var outcome = await commentService.DeleteAsync(
            id,
            visitId,
            careRecipientId.Value,
            expectedVersion,
            cancellationToken
        );

        return outcome is WriteOutcome.Succeeded ? NoContent() : this.Refused(outcome);
    }
}
