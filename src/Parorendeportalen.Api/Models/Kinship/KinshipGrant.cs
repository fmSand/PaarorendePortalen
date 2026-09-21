namespace Parorendeportalen.Api.Models.Kinship;

public class KinshipGrant
{
    public int Id { get; set; }

    public int NextOfKinId { get; set; }

    public NextOfKin NextOfKin { get; set; } = null!;

    public int CareRecipientId { get; set; }

    public CareRecipient CareRecipient { get; set; } = null!;

    public string? Relationship { get; set; }

    public required DateTimeOffset ValidFrom { get; set; }

    public DateTimeOffset? ValidTo { get; set; }
}
