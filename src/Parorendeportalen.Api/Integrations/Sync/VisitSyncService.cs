using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Integrations.Sync;

public sealed class VisitSyncService(
    IVisitIngestionStore ingestionStore,
    ICareRecipientRepository careRecipients,
    NationalIdHasher nationalIdHasher,
    ILogger<VisitSyncService> logger
) : IVisitSyncService
{
    // A source that keeps issuing continuation tokens would otherwise hold the
    // loop open. What is left over resumes from the token on the next run.
    private const int MaxPagesPerRun = 100;

    public async Task<VisitSyncOutcome> RunAsync(
        IVisitSource source,
        SyncPosition resumeFrom,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(resumeFrom);

        var cursor = resumeFrom.ToCursor();

        var inserted = 0;
        var updated = 0;
        var unchanged = 0;
        var unresolvedCount = 0;
        DateTimeOffset? newest = null;
        var oldestUnresolved = resumeFrom.UnresolvedFrom;
        string? pendingToken = null;

        for (var page = 0; page < MaxPagesPerRun; page++)
        {
            var fetched = await source.FetchVisitsChangedSinceAsync(cursor, cancellationToken);

            var (visits, unresolved) = await ResolveAsync(fetched.Snapshots, cancellationToken);

            foreach (var snapshot in fetched.Snapshots)
            {
                newest = Newest(newest, snapshot.SourceUpdatedAt);
            }

            foreach (var snapshot in unresolved)
            {
                oldestUnresolved = Oldest(oldestUnresolved, snapshot.SourceUpdatedAt);
            }

            unresolvedCount += unresolved.Count;

            var result = await ingestionStore.UpsertAsync(visits, cancellationToken);
            inserted += result.Inserted;
            updated += result.Updated;
            unchanged += result.Unchanged;

            pendingToken = fetched.HasMore ? fetched.ContinuationToken : null;
            if (pendingToken is null)
            {
                break;
            }

            cursor = cursor.Next(pendingToken);
        }

        if (unresolvedCount > 0)
        {
            logger.LogWarning(
                "{Count} visit snapshots from {SourceSystem} name a care recipient this portal does not hold.",
                unresolvedCount,
                source.SourceSystem
            );
        }

        var ingestion = new VisitIngestionResult(inserted, updated, unchanged);

        // A token still in hand means the page cap stopped the run mid-stream.
        // The watermark stays where it was, since the token positions inside
        // the stream that watermark opened and advancing it would strand the
        // pages behind it. The holdback rides along for the same reason.
        if (pendingToken is not null)
        {
            logger.LogWarning(
                "Sync of {SourceSystem} stopped at the page cap and will resume where it left off.",
                source.SourceSystem
            );

            return new VisitSyncOutcome(
                ingestion,
                unresolvedCount,
                new SyncPosition(resumeFrom.SourceUpdatedThrough, pendingToken, oldestUnresolved),
                Truncated: true
            );
        }

        // The holdback is dropped: the watermark now sits at or before what did
        // not resolve, so the next run reads it again and derives it itself.
        var through = WatermarkThrough(newest, oldestUnresolved);

        // A run that read nothing still has to clear a token the page cap left
        // behind, or the source keeps filtering out everything before it.
        return new VisitSyncOutcome(
            ingestion,
            unresolvedCount,
            through is null && resumeFrom.ContinuationToken is null
                ? null
                : new SyncPosition(through, null)
        );
    }

    // Held back to the oldest snapshot that did not resolve, so those visits
    // arrive on their own once the care recipient is seeded.
    private static DateTimeOffset? WatermarkThrough(
        DateTimeOffset? newest,
        DateTimeOffset? oldestUnresolved
    ) =>
        (newest, oldestUnresolved) switch
        {
            (null, _) => null,
            (_, null) => newest,
            var (seen, unresolved) => unresolved < seen ? unresolved : seen,
        };

    private async Task<(List<Visit> Visits, List<VisitSnapshot> Unresolved)> ResolveAsync(
        IReadOnlyList<VisitSnapshot> snapshots,
        CancellationToken cancellationToken
    )
    {
        var hashes = snapshots
            .Select(snapshot => nationalIdHasher.Hash(snapshot.CareRecipient.HashInput))
            .ToList();

        var careRecipientIds = await careRecipients.GetIdsByNationalIdHashesAsync(
            [.. hashes.Distinct(StringComparer.Ordinal)],
            cancellationToken
        );

        var visits = new List<Visit>(snapshots.Count);
        var unresolved = new List<VisitSnapshot>();

        for (var index = 0; index < snapshots.Count; index++)
        {
            if (careRecipientIds.TryGetValue(hashes[index], out var careRecipientId))
            {
                visits.Add(ToVisit(snapshots[index], careRecipientId));
            }
            else
            {
                unresolved.Add(snapshots[index]);
            }
        }

        return (visits, unresolved);
    }

    private static Visit ToVisit(VisitSnapshot snapshot, int careRecipientId) =>
        new()
        {
            CareRecipientId = careRecipientId,
            ScheduledAt = snapshot.ScheduledAt,
            ActualAt = snapshot.ActualAt,
            Status = snapshot.Status,
            CaregiverName = snapshot.CaregiverName,
            Notes = snapshot.Notes,
            Origin = snapshot.SourceSystem.ToOrigin(),
            ExternalId = snapshot.ExternalId,
        };

    private static DateTimeOffset Newest(DateTimeOffset? current, DateTimeOffset candidate) =>
        current is null || candidate > current ? candidate : current.Value;

    private static DateTimeOffset Oldest(DateTimeOffset? current, DateTimeOffset candidate) =>
        current is null || candidate < current ? candidate : current.Value;
}
