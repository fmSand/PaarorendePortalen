using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Controllers;

public static class WriteResults
{
    // Required on every unsafe method here, or two next-of-kin editing one entry lose each
    // other's work silently. Returns the response to send, or null to carry on.
    public static ActionResult? ReadExpectedVersion(
        this ControllerBase controller,
        out uint version
    )
    {
        ArgumentNullException.ThrowIfNull(controller);

        version = 0;
        var header = controller.Request.Headers.IfMatch.ToString();

        if (string.IsNullOrWhiteSpace(header))
        {
            return controller.Problem(
                title: "If-Match is required.",
                detail: "Send the version the entry carried when you last read it.",
                statusCode: StatusCodes.Status428PreconditionRequired
            );
        }

        // Reject weak tags (compare loosely) and "*" (waives the check): neither belongs in a lost-update guard.
        if (
            !EntityTagHeaderValue.TryParse(header, out var tag)
            || tag.IsWeak
            || !uint.TryParse(tag.Tag.Value?.Trim('"'), out version)
        )
        {
            return controller.Problem(
                title: "If-Match is not a version this API issued.",
                detail: "Use the ETag from the last read of this entry.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        return null;
    }

    public static void SetETag(this ControllerBase controller, uint version)
    {
        ArgumentNullException.ThrowIfNull(controller);

        controller.Response.Headers.ETag = $"\"{version}\"";
    }

    public static ActionResult Refused(this ControllerBase controller, WriteOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(controller);

        return outcome switch
        {
            WriteOutcome.NotFound => controller.NotFound(),
            WriteOutcome.NotAuthor => controller.Problem(
                title: "Only the author may change this entry.",
                detail: "Another next-of-kin wrote it, and it stays theirs to edit.",
                statusCode: StatusCodes.Status403Forbidden
            ),
            WriteOutcome.SourceOwned => controller.Problem(
                title: "This entry was not written in the portal.",
                detail: "A visit reported by the municipality is changed there, not here.",
                statusCode: StatusCodes.Status403Forbidden
            ),
            // 412: the UPDATE's WHERE clause is what evaluated the If-Match precondition.
            WriteOutcome.VersionConflict => controller.Problem(
                title: "The entry changed while you were editing it.",
                detail: "Read it again and reapply your change.",
                statusCode: StatusCodes.Status412PreconditionFailed
            ),
            _ => throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "No response is defined for this write outcome."
            ),
        };
    }
}
