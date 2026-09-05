namespace Parorendeportalen.Api.Models;

public enum ChangeKind
{
    // No zero value: an unset kind must not read as a real change.
    Added = 1,
    Rescheduled = 2,
    Completed = 3,
    Cancelled = 4,
    Missed = 5,
    Updated = 6,
}
