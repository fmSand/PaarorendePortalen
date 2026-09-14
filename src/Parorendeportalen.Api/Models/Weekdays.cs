namespace Parorendeportalen.Api.Models;

// System.DayOfWeek starts at Sunday = 0, which cannot be a flag, so the days
// are redeclared here. Use Weekdays.Of(date) rather than casting between them.
[Flags]
public enum Weekdays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,

    WeekdaysOnly = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekend = Saturday | Sunday,
    EveryDay = WeekdaysOnly | Weekend,
}

public static class WeekdaysExtensions
{
    public static Weekdays Of(DayOfWeek day) =>
        day switch
        {
            DayOfWeek.Monday => Weekdays.Monday,
            DayOfWeek.Tuesday => Weekdays.Tuesday,
            DayOfWeek.Wednesday => Weekdays.Wednesday,
            DayOfWeek.Thursday => Weekdays.Thursday,
            DayOfWeek.Friday => Weekdays.Friday,
            DayOfWeek.Saturday => Weekdays.Saturday,
            DayOfWeek.Sunday => Weekdays.Sunday,
            _ => throw new ArgumentOutOfRangeException(nameof(day), day, "Not a day of the week."),
        };

    public static Weekdays Of(DateOnly date) => Of(date.DayOfWeek);

    public static bool Covers(this Weekdays days, DateOnly date) => days.HasFlag(Of(date));

    // Monday first, the way a Norwegian week is written and read.
    private static readonly DayOfWeek[] WeekOrder =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday,
    ];

    public static IReadOnlyList<DayOfWeek> ToDays(this Weekdays days) =>
        [.. WeekOrder.Where(day => days.HasFlag(Of(day)))];
}
