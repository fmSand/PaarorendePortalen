namespace Parorendeportalen.Api.Dtos.Notifications;

public sealed record SetNotificationPreferenceRequest
{
    public required bool Enabled { get; init; }
}
