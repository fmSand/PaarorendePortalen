using Parorendeportalen.Api.Dtos.Visits;
using Parorendeportalen.Api.Models.Visits;
using Parorendeportalen.Api.Repositories.Visits;
using Parorendeportalen.Api.Services.Kinship;

namespace Parorendeportalen.Api.Services.Visits;

public sealed class VisitCommentService(
    IVisitCommentRepository comments,
    IVisitRepository visits,
    ICurrentNextOfKinAccessor currentNextOfKin,
    TimeProvider timeProvider
) : IVisitCommentService
{
    public async Task<IReadOnlyList<VisitCommentResponse>?> GetByVisitIdAsync(
        int visitId,
        int careRecipientId,
        CancellationToken cancellationToken
    )
    {
        var author = await ViewerIdAsync(cancellationToken);

        if (!await VisitIsVisibleAsync(visitId, careRecipientId, author, cancellationToken))
        {
            return null;
        }

        var thread = await comments.GetByVisitIdAsync(visitId, author, cancellationToken);

        return thread.Select(c => c.ToResponse()).ToList();
    }

    public async Task<WriteResult<VisitCommentResponse>> CreateAsync(
        int visitId,
        int careRecipientId,
        CreateVisitCommentRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var author = await ViewerIdAsync(cancellationToken);

        if (!await VisitIsVisibleAsync(visitId, careRecipientId, author, cancellationToken))
        {
            return WriteResult<VisitCommentResponse>.Failed(WriteOutcome.NotFound);
        }

        var comment = new VisitComment
        {
            VisitId = visitId,
            AuthorNextOfKinId = author,
            Body = request.Body,
            Visibility = request.Visibility,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        await comments.AddAsync(comment, cancellationToken);

        return WriteResult<VisitCommentResponse>.Succeeded(
            await ReadBackAsync(comment.Id, cancellationToken)
        );
    }

    public async Task<WriteResult<VisitCommentResponse>> UpdateAsync(
        int id,
        int visitId,
        int careRecipientId,
        UpdateVisitCommentRequest request,
        uint expectedVersion,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var author = await ViewerIdAsync(cancellationToken);

        if (!await VisitIsVisibleAsync(visitId, careRecipientId, author, cancellationToken))
        {
            return WriteResult<VisitCommentResponse>.Failed(WriteOutcome.NotFound);
        }

        var comment = await comments.GetForWriteAsync(id, visitId, cancellationToken);
        var refusal = Refusal(comment, author);
        if (refusal is not null)
        {
            return WriteResult<VisitCommentResponse>.Failed(refusal.Value);
        }

        comment!.Body = request.Body;
        comment.Visibility = request.Visibility;
        comment.UpdatedAt = timeProvider.GetUtcNow();

        if (!await comments.UpdateAsync(comment, expectedVersion, cancellationToken))
        {
            return WriteResult<VisitCommentResponse>.Failed(WriteOutcome.VersionConflict);
        }

        return WriteResult<VisitCommentResponse>.Succeeded(
            await ReadBackAsync(id, cancellationToken)
        );
    }

    public async Task<WriteOutcome> DeleteAsync(
        int id,
        int visitId,
        int careRecipientId,
        uint expectedVersion,
        CancellationToken cancellationToken
    )
    {
        var author = await ViewerIdAsync(cancellationToken);

        if (!await VisitIsVisibleAsync(visitId, careRecipientId, author, cancellationToken))
        {
            return WriteOutcome.NotFound;
        }

        var comment = await comments.GetForWriteAsync(id, visitId, cancellationToken);
        var refusal = Refusal(comment, author);
        if (refusal is not null)
        {
            return refusal.Value;
        }

        return await comments.DeleteAsync(comment!, expectedVersion, cancellationToken)
            ? WriteOutcome.Succeeded
            : WriteOutcome.VersionConflict;
    }

    // A comment the caller can't see answers NotFound: 403 would confirm it exists.
    private static WriteOutcome? Refusal(VisitComment? comment, int author) =>
        comment switch
        {
            null => WriteOutcome.NotFound,
            var c when !c.IsVisibleTo(author) => WriteOutcome.NotFound,
            var c when c.AuthorNextOfKinId != author => WriteOutcome.NotAuthor,
            _ => null,
        };

    // The visit's own visibility scope decides if the thread exists at all.
    private async Task<bool> VisitIsVisibleAsync(
        int visitId,
        int careRecipientId,
        int viewer,
        CancellationToken cancellationToken
    ) => await visits.GetByIdAsync(visitId, careRecipientId, viewer, cancellationToken) is not null;

    // Read back: it hasn't loaded the author's display name.
    private async Task<VisitCommentResponse> ReadBackAsync(
        int id,
        CancellationToken cancellationToken
    )
    {
        var comment = await comments.GetByIdAsync(id, cancellationToken);

        return comment!.ToResponse();
    }

    private async Task<int> ViewerIdAsync(CancellationToken cancellationToken)
    {
        var current =
            await currentNextOfKin.GetCurrentAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "The session resolves to no next-of-kin, so no comment can be read or written for it."
            );

        return current.NextOfKinId;
    }
}
