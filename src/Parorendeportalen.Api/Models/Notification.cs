namespace Parorendeportalen.Api.Models;

// One next-of-kin's copy of a change event. Holds no notes and no caregiver
// name, since the row can outlive the consent it was written under.
public class Notification
{
    public long Id { get; set; }

    public int NextOfKinId { get; set; }

    public NextOfKin NextOfKin { get; set; } = null!;

    public int CareRecipientId { get; set; }

    public CareRecipient CareRecipient { get; set; } = null!;

    // No FK: the inbox row outlives the outbox row it came from.
    public long ChangeEventId { get; set; }

    public DataCategory Category { get; set; }

    public ChangeKind Kind { get; set; }

    // No FK either. A visit gone from the log leaves a notice a client 404s on.
    public int? VisitId { get; set; }

    public DateTimeOffset? ScheduledAt { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }
}
