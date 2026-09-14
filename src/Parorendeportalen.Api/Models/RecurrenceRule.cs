namespace Parorendeportalen.Api.Models;

// "Hjemmesykepleie x2/dag man-fre" as something expandable: which weekdays, and
// how many times on each of them. No interval, so "annenhver uke" can't be
// expressed; nothing in the source material needs one, and a rule the day plan
// can't expand is worse than a rule it refuses to hold.
public sealed record RecurrenceRule
{
    private readonly Weekdays _days;
    private readonly int _timesPerDay;

    // None would expand to an empty day plan for a vedtak that is in force,
    // which reads as "nothing is due today" rather than as the bad data it is.
    public required Weekdays Days
    {
        get => _days;
        init
        {
            if (value == Weekdays.None)
            {
                throw new ArgumentException(
                    "A recurrence rule must name at least one weekday.",
                    nameof(value)
                );
            }

            if ((value & ~Weekdays.EveryDay) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "A recurrence rule holds days of the week and nothing else."
                );
            }

            _days = value;
        }
    }

    public required int TimesPerDay
    {
        get => _timesPerDay;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxTimesPerDay);
            _timesPerDay = value;
        }
    }

    // Well past any real vedtak; bounds what one malformed row can expand into.
    public const int MaxTimesPerDay = 12;

    public bool Covers(DateOnly date) => Days.Covers(date);
}
