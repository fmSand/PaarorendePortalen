using Parorendeportalen.Api.Models.Kinship;

namespace Parorendeportalen.Api.Models.Notifications;

// No row means enabled. A row records the choice either way.
public class NotificationPreference
{
    public int Id { get; set; }

    public int NextOfKinId { get; set; }

    public NextOfKin NextOfKin { get; set; } = null!;

    public ChangeKind Kind { get; set; }

    public bool Enabled { get; set; }
}
