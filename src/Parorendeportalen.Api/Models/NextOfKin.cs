namespace Parorendeportalen.Api.Models;

public class NextOfKin
{
    public int Id { get; set; }

    public int CareRecipientId { get; set; }

    public CareRecipient CareRecipient { get; set; } = null!;

    public string? ExternalId { get; set; }

    public required string NationalIdHash { get; set; }

    public required string DisplayName { get; set; }

    public string? Relationship { get; set; }

    public DateTimeOffset ValidFrom { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ValidTo { get; set; }
}
