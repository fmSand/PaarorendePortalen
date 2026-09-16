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

    // Next-of-kin's own title; a source names visits by service type instead, so synced rows have none.
    public string? Title { get; set; }

    public Origin Origin { get; set; }

    // Which municipal service this was, when the source says it. Null for a portal-authored
    // visit or one a source sent without it; the day plan matches on it, so null never settles.
    public ServiceType? ServiceType { get; set; }

    // Source's own id (what sync upserts on). Null for Portal rows.
    public string? ExternalId { get; set; }

    // Null together with Visibility: an author-less row is governed by consent alone,
    // so it can't default to Private.
    public int? CreatedByNextOfKinId { get; set; }

    public NextOfKin? CreatedBy { get; set; }

    public Visibility? Visibility { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public List<VisitComment> Comments { get; set; } = [];

    public uint Version { get; set; }
}
