namespace Parorendeportalen.Api.Models.Access;

public class AccessLogEntry
{
    public long Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public int NextOfKinId { get; set; }

    public int CareRecipientId { get; set; }

    public DataCategory Category { get; set; }

    public AccessOperation Operation { get; set; }

    public AccessDecision Outcome { get; set; }
}
