namespace Parorendeportalen.Api.Models;

public class NextOfKin
{
    public int Id { get; set; }

    public string? ExternalId { get; set; }

    public required string NationalIdHash { get; set; }

    public required string DisplayName { get; set; }

    public List<KinshipGrant> Grants { get; set; } = [];
}
