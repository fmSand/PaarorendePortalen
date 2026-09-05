using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class NotificationsController(
    INotificationService notificationService,
    IHealthDataAccessPolicy accessPolicy,
    ICurrentNextOfKinAccessor currentNextOfKin
) : ControllerBase
{
    // The policy decides the scope and logs it. A caller with grants and no
    // consent gets an empty inbox, since no category was asked for by name.
    [HttpGet]
    [ProducesResponseType(typeof(NotificationInboxResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationInboxResponse>> Get(
        CancellationToken cancellationToken
    )
    {
        var access = await accessPolicy.AuthorizeConsentedReadsAsync(cancellationToken);
        if (access is null)
        {
            return NotFound();
        }

        var inbox = await notificationService.GetInboxAsync(
            access.NextOfKinId,
            access.Scopes,
            cancellationToken
        );
        return Ok(inbox);
    }

    // No access log, since the write reads no health data. Still the inbox's
    // scope: a notice the caller cannot see must not be markable either.
    [HttpPost("{id:long}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(long id, CancellationToken cancellationToken)
    {
        var access = await accessPolicy.ResolveConsentedScopeAsync(cancellationToken);
        if (access is null)
        {
            return NotFound();
        }

        var marked = await notificationService.MarkReadAsync(
            access.NextOfKinId,
            access.Scopes,
            id,
            cancellationToken
        );
        return marked ? NoContent() : NotFound();
    }

    [HttpPost("read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        var access = await accessPolicy.ResolveConsentedScopeAsync(cancellationToken);
        if (access is null)
        {
            return NotFound();
        }

        await notificationService.MarkAllReadAsync(
            access.NextOfKinId,
            access.Scopes,
            cancellationToken
        );
        return NoContent();
    }

    [HttpGet("preferences")]
    [ProducesResponseType(
        typeof(IReadOnlyList<NotificationPreferenceResponse>),
        StatusCodes.Status200OK
    )]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<NotificationPreferenceResponse>>> GetPreferences(
        CancellationToken cancellationToken
    )
    {
        var current = await currentNextOfKin.GetCurrentAsync(cancellationToken);
        if (current is null)
        {
            return NotFound();
        }

        var preferences = await notificationService.GetPreferencesAsync(
            current.NextOfKinId,
            cancellationToken
        );
        return Ok(preferences);
    }

    // Route binding accepts any integer for an enum, so the guard is here where it can be a 400.
    [HttpPut("preferences/{kind}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetPreference(
        ChangeKind kind,
        [FromBody] SetNotificationPreferenceRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!Enum.IsDefined(kind))
        {
            ModelState.AddModelError(nameof(kind), "Unknown notification kind.");
            return ValidationProblem(ModelState);
        }

        var current = await currentNextOfKin.GetCurrentAsync(cancellationToken);
        if (current is null)
        {
            return NotFound();
        }

        await notificationService.SetPreferenceAsync(
            current.NextOfKinId,
            kind,
            request.Enabled!.Value,
            cancellationToken
        );
        return NoContent();
    }
}
