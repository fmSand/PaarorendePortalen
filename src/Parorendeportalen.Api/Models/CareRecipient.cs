namespace Parorendeportalen.Api.Models;

public class CareRecipient
{
    public int Id { get; set; }

    public required string Name { get; set; }

    // Peppered HMAC of Nation<alIdentifier.HashInput. Null when the portal does
    // not hold the number (sync skips).
    public string? NationalIdHash { get; set; }

    public List<Visit> Visits { get; set; } = [];

    public List<KinshipGrant> Grants { get; set; } = [];
}
