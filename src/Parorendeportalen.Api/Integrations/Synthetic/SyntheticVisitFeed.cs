using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Integrations.Synthetic;

// The dataset SyntheticVisitSource serves: a rolling window around the instant
// it is built, the way a source with a retention window behaves.
public static class SyntheticVisitFeed
{
    private const int DaysBack = 7;
    private const int DaysAhead = 7;

    private static readonly TimeSpan[] Slots = [TimeSpan.FromHours(8), TimeSpan.FromHours(16)];

    public static IReadOnlyList<VisitSnapshot> Build(
        IReadOnlyList<SyntheticRecipient> careRecipients,
        DateTimeOffset now
    )
    {
        ArgumentNullException.ThrowIfNull(careRecipients);

        var midnight = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var snapshots = new List<VisitSnapshot>();

        for (var recipient = 0; recipient < careRecipients.Count; recipient++)
        {
            // One publication time per recipient, so the planned visits share a
            // SourceUpdatedAt and paging has to survive the tie. Spread in
            // seconds to stay below 08:05, where finishing the 08:00 visit
            // lands: a snapshot that moves earlier is one paging can skip.
            var publishedAt = midnight.AddHours(5).AddSeconds(recipient);

            for (var day = -DaysBack; day <= DaysAhead; day++)
            {
                for (var slot = 0; slot < Slots.Length; slot++)
                {
                    var scheduledAt = midnight.AddDays(day) + Slots[slot];

                    // The upsert keys on this, so it has to name the same visit
                    // tomorrow as it does today, after the window has rolled.
                    var externalId =
                        $"synthetic-{careRecipients[recipient].Key}-{scheduledAt:yyyyMMdd}-{slot:D1}";

                    snapshots.Add(
                        Snapshot(
                            careRecipients[recipient].Identifier,
                            externalId,
                            scheduledAt,
                            publishedAt,
                            now
                        )
                    );
                }
            }
        }

        return snapshots;
    }

    private static VisitSnapshot Snapshot(
        NationalIdentifier careRecipient,
        string externalId,
        DateTimeOffset scheduledAt,
        DateTimeOffset publishedAt,
        DateTimeOffset now
    )
    {
        if (scheduledAt >= now)
        {
            return new VisitSnapshot
            {
                SourceSystem = SourceSystem.Synthetic,
                ExternalId = externalId,
                CareRecipient = careRecipient,
                SourceUpdatedAt = publishedAt,
                ScheduledAt = scheduledAt,
                Status = VisitStatus.Planned,
                CaregiverName = "Hjemmetjenesten Oslo",
            };
        }

        var missed = scheduledAt.DayOfYear % 5 == 0;

        return new VisitSnapshot
        {
            SourceSystem = SourceSystem.Synthetic,
            ExternalId = externalId,
            CareRecipient = careRecipient,
            SourceUpdatedAt = missed ? scheduledAt.AddMinutes(30) : scheduledAt.AddMinutes(5),
            ScheduledAt = scheduledAt,
            ActualAt = missed ? null : scheduledAt.AddMinutes(5),
            Status = missed ? VisitStatus.Missed : VisitStatus.Completed,
            CaregiverName = "Hjemmetjenesten Oslo",
            Notes =
                missed ? "Ingen oppmøte registrert."
                : scheduledAt.Hour < 12 ? "Morgenstell og medisiner gitt."
                : "Tilsyn og måltidsstøtte.",
        };
    }
}
