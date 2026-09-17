using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Dtos.Visits;

namespace Parorendeportalen.Api.Services.Visits;

public interface IVisitService
{
    Task<PagedResponse<VisitResponse>> GetByCareRecipientIdAsync(
        int careRecipientId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken
    );

    Task<VisitResponse?> GetByIdAsync(
        int id,
        int careRecipientId,
        CancellationToken cancellationToken
    );

    Task<VisitResponse> CreateAsync(
        CreateVisitRequest request,
        CancellationToken cancellationToken
    );

    // Author check here so a second caller can't skip.
    Task<WriteResult<VisitResponse>> UpdateAsync(
        int id,
        int careRecipientId,
        UpdateVisitRequest request,
        uint expectedVersion,
        CancellationToken cancellationToken
    );

    Task<WriteOutcome> DeleteAsync(
        int id,
        int careRecipientId,
        uint expectedVersion,
        CancellationToken cancellationToken
    );
}
