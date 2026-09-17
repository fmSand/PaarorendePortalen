namespace Parorendeportalen.Api.Services;

// A vedtak's weekdays are Norwegian calendar days, and visits are stored as
// instants in UTC. Which day a visit falls on is therefore a conversion:
// for a 01:30 visit in summer the UTC date is the day before.
public static class NorwegianTime
{
    public static TimeZoneInfo Zone { get; } = ResolveZone();

    public static DateOnly DateOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);

    // Half-open, and the last Sunday in March is 23 hours and October's is 25. Both
    // bounds come from the zone, so the short day keeps all its visits.
    public static (DateTimeOffset Start, DateTimeOffset End) BoundsOf(DateOnly date) =>
        (StartOf(date), StartOf(date.AddDays(1)));

    private static DateTimeOffset StartOf(DateOnly date)
    {
        // Norway's transitions happen at 02:00 and 03:00 local, so midnight is
        // never a time that does not exist or happens twice.
        var midnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        // The Oslo offset picks the instant, then the bound travels on as UTC like
        // every other instant in the app.
        return new DateTimeOffset(midnight, Zone.GetUtcOffset(midnight)).ToUniversalTime();
    }

    private static TimeZoneInfo ResolveZone() =>
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Oslo", out var zone)
            ? zone
            : throw new TimeZoneNotFoundException(
                "'Europe/Oslo' is not available on this host. A slim container image without "
                    + "tzdata is the usual cause. The day plan cannot decide which Norwegian "
                    + "day a visit belongs to without it."
            );
}
