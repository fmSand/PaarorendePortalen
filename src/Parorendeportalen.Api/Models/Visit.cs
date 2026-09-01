namespace Parorendeportalen.Api.Models;

public class Visit
{
    public int Id { get; set; }

    public int CareRecipientId { get; set; }

    public CareRecipient CareRecipient { get; set; } = null!;

    public DateTimeOffset ScheduledAt { get; set; }

    public DateTimeOffset? ActualAt { get; set; }

    public VisitStatus Status { get; set; }

    public string? CaregiverName { get; set; }

    public string? Notes { get; set; }

    public Origin Origin { get; set; }

    // Source's own id (what sync upserts on). Null for Portal rows.
    public string? ExternalId { get; set; }
}
