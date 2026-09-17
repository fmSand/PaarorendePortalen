namespace Parorendeportalen.Api.Models.Planning;

// "Hjemmesykepleie x2/dag man-fre": which weekdays, and how many times on each. No
// interval, so "annenhver uke" can't be expressed. Nothing in the source material needs one.
public sealed record RecurrenceRule
{
    private readonly Weekdays _days;
    private readonly int _timesPerDay;

    // None would give an empty day plan for a vedtak in force, which looks like nothing is due.
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

    // Well above what any vedtak grants. Caps what one bad row can expand into.
    public const int MaxTimesPerDay = 12;

    public bool Covers(DateOnly date) => Days.Covers(date);
}
