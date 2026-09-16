using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;

namespace Parorendeportalen.Api.Services;

public sealed class VisitService(
    IVisitRepository repository,
    ICurrentNextOfKinAccessor currentNextOfKin,
    TimeProvider timeProvider
) : IVisitService
{
    public async Task<PagedResponse<VisitResponse>> GetByCareRecipientIdAsync(
        int careRecipientId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken
    )
    {
        var viewer = await ViewerIdAsync(cancellationToken);

        var (visits, totalCount) = await repository.GetByCareRecipientIdAsync(
            careRecipientId,
            viewer,
            from,
            to,
            pageNumber,
            pageSize,
            cancellationToken
        );

        return new PagedResponse<VisitResponse>(
            visits.Select(v => v.ToResponse()).ToList(),
            pageNumber,
            pageSize,
            totalCount
        );
    }

    public async Task<VisitResponse?> GetByIdAsync(
        int id,
        int careRecipientId,
        CancellationToken cancellationToken
    )
    {
        var viewer = await ViewerIdAsync(cancellationToken);

        var visit = await repository.GetByIdAsync(id, careRecipientId, viewer, cancellationToken);
        return visit?.ToResponse();
    }

    public async Task<VisitResponse> CreateAsync(
        CreateVisitRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var author = await ViewerIdAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();

        var visit = new Visit
        {
            CareRecipientId = request.CareRecipientId,
            ScheduledAt = request.ScheduledAt,
            Status = VisitStatus.Planned,
            Title = request.Title,
            Notes = request.Notes,
            Origin = Origin.Portal,
            CreatedByNextOfKinId = author,
            Visibility = request.Visibility,
            CreatedAt = now,
        };

        await repository.AddAsync(visit, cancellationToken);

        // Read back: it hasn't loaded the care recipient/author names.
        var created = await repository.GetByIdAsync(
            visit.Id,
            visit.CareRecipientId,
            author,
            cancellationToken
        );

        return created!.ToResponse();
    }

    public async Task<WriteResult<VisitResponse>> UpdateAsync(
        int id,
        int careRecipientId,
        UpdateVisitRequest request,
        uint expectedVersion,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var author = await ViewerIdAsync(cancellationToken);

        var visit = await repository.GetForWriteAsync(id, careRecipientId, cancellationToken);
        var refusal = Refusal(visit, author);
        if (refusal is not null)
        {
            return WriteResult<VisitResponse>.Failed(refusal.Value);
        }

        visit!.ScheduledAt = request.ScheduledAt;
        visit.Title = request.Title;
        visit.Notes = request.Notes;
        visit.Visibility = request.Visibility;
        visit.UpdatedAt = timeProvider.GetUtcNow();

        if (!await repository.UpdateAsync(visit, expectedVersion, cancellationToken))
        {
            return WriteResult<VisitResponse>.Failed(WriteOutcome.VersionConflict);
        }

        var updated = await repository.GetByIdAsync(id, careRecipientId, author, cancellationToken);

        return WriteResult<VisitResponse>.Succeeded(updated!.ToResponse());
    }

    public async Task<WriteOutcome> DeleteAsync(
        int id,
        int careRecipientId,
        uint expectedVersion,
        CancellationToken cancellationToken
    )
    {
        var author = await ViewerIdAsync(cancellationToken);

        var visit = await repository.GetForWriteAsync(id, careRecipientId, cancellationToken);
        var refusal = Refusal(visit, author);
        if (refusal is not null)
        {
            return refusal.Value;
        }

        return await repository.DeleteAsync(visit!, expectedVersion, cancellationToken)
            ? WriteOutcome.Succeeded
            : WriteOutcome.VersionConflict;
    }

    // A private entry someone else wrote answers NotFound.
    private static WriteOutcome? Refusal(Visit? visit, int author) =>
        visit switch
        {
            null => WriteOutcome.NotFound,
            { CreatedByNextOfKinId: null } => WriteOutcome.SourceOwned,
            { Visibility: Visibility.Private } v when v.CreatedByNextOfKinId != author =>
                WriteOutcome.NotFound,
            var v when v.CreatedByNextOfKinId != author => WriteOutcome.NotAuthor,
            _ => null,
        };

    // Wiring bug if this throws: the access policy should already have answered 403/404.
    private async Task<int> ViewerIdAsync(CancellationToken cancellationToken)
    {
        var current =
            await currentNextOfKin.GetCurrentAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "The session resolves to no next-of-kin, so no visit can be read or written for it."
            );

        return current.NextOfKinId;
    }
}
