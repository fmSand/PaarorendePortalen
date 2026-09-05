namespace Parorendeportalen.Api.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    // Task.Delay refuses anything longer.
    public static readonly TimeSpan MaxPollInterval = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    public bool Enabled { get; init; } = true;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMinutes(1);

    // A full batch means more is waiting, and the worker goes straight back
    // for it instead of sleeping.
    public int BatchSize { get; init; } = 100;
}
