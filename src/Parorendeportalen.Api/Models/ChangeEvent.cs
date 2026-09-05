namespace Parorendeportalen.Api.Models;

// The outbox, saved together with the visit it describes. VisitId and
// ScheduledAt belong to the Visits category; a second category adds its own.
public class ChangeEvent
{
    public long Id { get; set; }

    public int CareRecipientId { get; set; }

    public CareRecipient CareRecipient { get; set; } = null!;

    public DataCategory Category { get; set; }

    public ChangeKind Kind { get; set; }

    public int? VisitId { get; set; }

    public Visit? Visit { get; set; }

    public DateTimeOffset? ScheduledAt { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }
}
