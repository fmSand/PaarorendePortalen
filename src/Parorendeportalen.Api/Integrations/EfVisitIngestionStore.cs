using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Integrations;

public sealed class EfVisitIngestionStore(AppDbContext context) : IVisitIngestionStore
{
    // Postgres holds timestamptz to the microsecond, in UTC. An incoming value
    // finer than that, or carrying an offset, would either report Updated on
    // every run or be refused by Npgsql.
    private const long TicksPerMicrosecond = 10;

    public async Task<VisitIngestionResult> UpsertAsync(
        IReadOnlyList<Visit> visits,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(visits);

        if (visits.Count == 0)
        {
            return new VisitIngestionResult(0, 0, 0);
        }

        var incoming = KeyByOriginAndExternalId(visits);
        var stored = await LoadStoredAsync(incoming.Keys, cancellationToken);

        var inserted = 0;
        var updated = 0;
        var unchanged = 0;

        foreach (var (key, visit) in incoming)
        {
            if (!stored.TryGetValue(key, out var row))
            {
                context.Visits.Add(visit);
                inserted++;
            }
            else if (Matches(row, visit))
            {
                unchanged++;
            }
            else
            {
                CopyPayload(from: visit, to: row);
                updated++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        return new VisitIngestionResult(inserted, updated, unchanged);
    }

    private static Dictionary<(Origin Origin, string ExternalId), Visit> KeyByOriginAndExternalId(
        IReadOnlyList<Visit> visits
    )
    {
        var keyed = new Dictionary<(Origin, string), Visit>();

        foreach (var visit in visits)
        {
            if (visit.Origin == Origin.Portal)
            {
                throw new ArgumentException(
                    "Ingestion cannot write a Portal row, which is authored in this portal.",
                    nameof(visits)
                );
            }

            if (string.IsNullOrWhiteSpace(visit.ExternalId))
            {
                throw new ArgumentException(
                    "An ingested visit needs the source's own id to upsert on.",
                    nameof(visits)
                );
            }

            visit.ScheduledAt = ToStoredPrecision(visit.ScheduledAt);
            visit.ActualAt = visit.ActualAt is { } actualAt ? ToStoredPrecision(actualAt) : null;

            // Deduplicating instead would hide a source contradicting itself
            // inside one batch, and the last write would silently win.
            if (!keyed.TryAdd((visit.Origin, visit.ExternalId), visit))
            {
                throw new ArgumentException(
                    $"'{visit.ExternalId}' appears twice in one batch under {visit.Origin}.",
                    nameof(visits)
                );
            }
        }

        return keyed;
    }

    private async Task<Dictionary<(Origin Origin, string ExternalId), Visit>> LoadStoredAsync(
        IReadOnlyCollection<(Origin Origin, string ExternalId)> keys,
        CancellationToken cancellationToken
    )
    {
        var externalIds = keys.Select(key => key.ExternalId).ToList();

        // Tracked, since the matches are what an update writes through.
        var rows = await context
            .Visits.Where(v =>
                v.Origin != Origin.Portal
                && v.ExternalId != null
                && externalIds.Contains(v.ExternalId)
            )
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => (row.Origin, row.ExternalId!));
    }

    private static bool Matches(Visit stored, Visit incoming) =>
        stored.CareRecipientId == incoming.CareRecipientId
        && stored.ScheduledAt == incoming.ScheduledAt
        && stored.ActualAt == incoming.ActualAt
        && stored.Status == incoming.Status
        && stored.CaregiverName == incoming.CaregiverName
        && stored.Notes == incoming.Notes;

    private static void CopyPayload(Visit from, Visit to)
    {
        to.CareRecipientId = from.CareRecipientId;
        to.ScheduledAt = from.ScheduledAt;
        to.ActualAt = from.ActualAt;
        to.Status = from.Status;
        to.CaregiverName = from.CaregiverName;
        to.Notes = from.Notes;
    }

    private static DateTimeOffset ToStoredPrecision(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();

        return utc.AddTicks(-(utc.Ticks % TicksPerMicrosecond));
    }
}
