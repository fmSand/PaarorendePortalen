namespace Parorendeportalen.Api.Models;

// Kinship says who, consent says what. Revoked by closing with ValidTo, never
// deleted, so the history stays for legal traceability.
public class Consent
{
    public int Id { get; set; }

    public int CareRecipientId { get; set; }

    public CareRecipient CareRecipient { get; set; } = null!;

    public int NextOfKinId { get; set; }

    public NextOfKin NextOfKin { get; set; } = null!;

    public DataCategory Category { get; set; }

    public DateTimeOffset ValidFrom { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ValidTo { get; set; }
}
