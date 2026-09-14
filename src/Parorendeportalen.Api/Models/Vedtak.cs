namespace Parorendeportalen.Api.Models;

// A municipal decision granting a service. Dated rather than timestamped: a
// vedtak runs from a date to a date, and the day plan asks about days.
public class Vedtak
{
    public int Id { get; set; }

    public int CareRecipientId { get; set; }

    public CareRecipient CareRecipient { get; set; } = null!;

    public ServiceType ServiceType { get; set; }

    // The municipality's own wording, kept verbatim so the portal shows what the
    // letter said rather than a sentence rebuilt from ServiceType and the rule.
    public string Title { get; set; } = string.Empty;

    public RecurrenceRule Recurrence { get; set; } = null!;

    public DateOnly ValidFrom { get; set; }

    // Open-ended until the municipality closes it.
    public DateOnly? ValidTo { get; set; }

    public VedtakStatus Status { get; set; }

    public List<VedtakTask> Tasks { get; } = [];

    // The authority on what the day plan may expand; EfVedtakRepository states the same rule in SQL.
    public bool IsInForceOn(DateOnly date) =>
        Status == VedtakStatus.Active && ValidFrom <= date && (ValidTo is null || date <= ValidTo);
}
