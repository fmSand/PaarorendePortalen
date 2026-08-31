namespace Parorendeportalen.Api.Models;

// One grant per (next-of-kin, care recipient) pair
public class KinshipGrant
{
    public int Id { get; set; }

    public int NextOfKinId { get; set; }

    public NextOfKin NextOfKin { get; set; } = null!;

    public int CareRecipientId { get; set; }

    public CareRecipient CareRecipient { get; set; } = null!;

    public string? Relationship { get; set; }

    public DateTimeOffset ValidFrom { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ValidTo { get; set; }
}
