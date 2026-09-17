using Parorendeportalen.Api.Dtos.Visits;

namespace Parorendeportalen.Api.Services.Visits;

public interface IVisitCommentService
{
    // Null when the visit is not one the caller can see, so the endpoint can
    // answer 404 without a second lookup.
    Task<IReadOnlyList<VisitCommentResponse>?> GetByVisitIdAsync(
        int visitId,
        int careRecipientId,
        CancellationToken cancellationToken
    );

    Task<WriteResult<VisitCommentResponse>> CreateAsync(
        int visitId,
        int careRecipientId,
        CreateVisitCommentRequest request,
        CancellationToken cancellationToken
    );

    Task<WriteResult<VisitCommentResponse>> UpdateAsync(
        int id,
        int visitId,
        int careRecipientId,
        UpdateVisitCommentRequest request,
        uint expectedVersion,
        CancellationToken cancellationToken
    );

    Task<WriteOutcome> DeleteAsync(
        int id,
        int visitId,
        int careRecipientId,
        uint expectedVersion,
        CancellationToken cancellationToken
    );
}
