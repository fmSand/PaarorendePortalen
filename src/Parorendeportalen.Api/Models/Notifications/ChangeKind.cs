namespace Parorendeportalen.Api.Models.Notifications;

public enum ChangeKind
{
    // No zero value, so an unset kind can't pass for a change.
    Added = 1,
    Rescheduled = 2,
    Completed = 3,
    Cancelled = 4,
    Missed = 5,
    Updated = 6,
}
