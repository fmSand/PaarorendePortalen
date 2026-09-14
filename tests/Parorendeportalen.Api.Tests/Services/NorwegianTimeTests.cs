using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Tests.Services;

public class NorwegianTimeTests
{
    // Norway moves its clocks on the last Sunday in March and in October: the 29th and the 25th in 2026.
    private static readonly DateOnly SpringForward = new(2026, 3, 29);
    private static readonly DateOnly FallBack = new(2026, 10, 25);

    [Fact]
    public void DateOf_JustAfterMidnightInSummer_IsTheNorwegianDayNotTheUtcOne()
    {
        // 01:30 on 2 July in Oslo, which UTC still calls 1 July.
        var instant = new DateTimeOffset(2026, 7, 1, 23, 30, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 7, 2), NorwegianTime.DateOf(instant));
    }

    [Fact]
    public void DateOf_JustAfterMidnightInWinter_IsTheNorwegianDayNotTheUtcOne()
    {
        // The offset is only one hour in January, so the boundary sits elsewhere.
        var instant = new DateTimeOffset(2026, 1, 1, 23, 30, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 1, 2), NorwegianTime.DateOf(instant));
    }

    [Fact]
    public void DateOf_JustBeforeMidnightInSummer_IsStillTheSameNorwegianDay()
    {
        // 23:30 on 1 July in Oslo.
        var instant = new DateTimeOffset(2026, 7, 1, 21, 30, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 7, 1), NorwegianTime.DateOf(instant));
    }

    [Fact]
    public void BoundsOf_OrdinaryDay_IsTwentyFourHoursWide()
    {
        var (start, end) = NorwegianTime.BoundsOf(new DateOnly(2026, 7, 1));

        Assert.Equal(TimeSpan.FromHours(24), end - start);
        Assert.Equal(TimeSpan.FromHours(2), start.Offset);
    }

    [Fact]
    public void BoundsOf_TheDayTheClocksGoForward_IsTwentyThreeHoursWide()
    {
        var (start, end) = NorwegianTime.BoundsOf(SpringForward);

        // Arithmetic on the start would have produced 24, swallowing an hour of visits.
        Assert.Equal(TimeSpan.FromHours(23), end - start);
        Assert.Equal(TimeSpan.FromHours(1), start.Offset);
        Assert.Equal(TimeSpan.FromHours(2), end.Offset);
    }

    [Fact]
    public void BoundsOf_TheDayTheClocksGoBack_IsTwentyFiveHoursWide()
    {
        var (start, end) = NorwegianTime.BoundsOf(FallBack);

        Assert.Equal(TimeSpan.FromHours(25), end - start);
        Assert.Equal(TimeSpan.FromHours(2), start.Offset);
        Assert.Equal(TimeSpan.FromHours(1), end.Offset);
    }

    [Fact]
    public void BoundsOf_TheRepeatedHour_FallsInsideTheDayItBelongsTo()
    {
        var (start, end) = NorwegianTime.BoundsOf(FallBack);

        // 02:30 happens twice that morning. Both instants are that Sunday.
        var firstPass = new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero);
        var secondPass = new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero);

        Assert.InRange(firstPass, start, end);
        Assert.InRange(secondPass, start, end);
        Assert.Equal(FallBack, NorwegianTime.DateOf(firstPass));
        Assert.Equal(FallBack, NorwegianTime.DateOf(secondPass));
    }

    [Fact]
    public void BoundsOf_EndOfOneDay_IsTheStartOfTheNext()
    {
        var (_, endOfFirst) = NorwegianTime.BoundsOf(new DateOnly(2026, 1, 31));
        var (startOfSecond, _) = NorwegianTime.BoundsOf(new DateOnly(2026, 2, 1));

        // Half-open and adjoining, so a visit at midnight lands on exactly one day.
        Assert.Equal(startOfSecond, endOfFirst);
    }

    [Fact]
    public void Zone_IsNorwegianAndObservesSummerTime()
    {
        Assert.True(NorwegianTime.Zone.SupportsDaylightSavingTime);
        Assert.Equal(
            TimeSpan.FromHours(1),
            NorwegianTime.Zone.GetUtcOffset(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc))
        );
        Assert.Equal(
            TimeSpan.FromHours(2),
            NorwegianTime.Zone.GetUtcOffset(new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc))
        );
    }
}
