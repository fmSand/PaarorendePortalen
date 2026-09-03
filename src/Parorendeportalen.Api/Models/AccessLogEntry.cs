namespace Parorendeportalen.Api.Models;

// Append-only. Internal ids and a category only, never a name or a national
// identifier, so the log cannot become the leak it exists to detect. No FKs, so
// a row outlives what it refers to.
public class AccessLogEntry
{
    public long Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public int NextOfKinId { get; set; }

    public int CareRecipientId { get; set; }

    public DataCategory Category { get; set; }

    public AccessDecision Outcome { get; set; }
}
