using Parorendeportalen.Api.Models.Planning;

namespace Parorendeportalen.Api.Tests.Models.Planning;

public class WeekdaysTests
{
    [Theory]
    [InlineData("2026-09-07", DayOfWeek.Monday, Weekdays.Monday)]
    [InlineData("2026-09-08", DayOfWeek.Tuesday, Weekdays.Tuesday)]
    [InlineData("2026-09-09", DayOfWeek.Wednesday, Weekdays.Wednesday)]
    [InlineData("2026-09-10", DayOfWeek.Thursday, Weekdays.Thursday)]
    [InlineData("2026-09-11", DayOfWeek.Friday, Weekdays.Friday)]
    [InlineData("2026-09-12", DayOfWeek.Saturday, Weekdays.Saturday)]
    [InlineData("2026-09-13", DayOfWeek.Sunday, Weekdays.Sunday)]
    public void Of_MapsEveryDayOfTheWeekToItsOwnFlag(
        string date,
        DayOfWeek expectedDay,
        Weekdays expectedFlag
    )
    {
        var parsed = DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

        // Sunday is 0 in System.DayOfWeek and 64 here; this is where the two numberings must agree.
        Assert.Equal(expectedDay, parsed.DayOfWeek);
        Assert.Equal(expectedFlag, WeekdaysExtensions.Of(parsed));
    }

    [Fact]
    public void ToDays_ListsMondayFirstWhateverOrderTheFlagsWereCombinedIn()
    {
        var days = (Weekdays.Sunday | Weekdays.Wednesday | Weekdays.Monday).ToDays();

        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Sunday], days);
    }

    [Fact]
    public void ToDays_EveryDay_ListsSevenDaysStartingOnMonday()
    {
        var days = Weekdays.EveryDay.ToDays();

        Assert.Equal(7, days.Count);
        Assert.Equal(DayOfWeek.Monday, days[0]);
        Assert.Equal(DayOfWeek.Sunday, days[6]);
    }

    [Fact]
    public void ToDays_None_IsEmpty()
    {
        Assert.Empty(Weekdays.None.ToDays());
    }

    [Fact]
    public void WeekdaysOnly_IsTheFiveWorkingDays()
    {
        Assert.Equal(5, Weekdays.WeekdaysOnly.ToDays().Count);
        Assert.DoesNotContain(DayOfWeek.Saturday, Weekdays.WeekdaysOnly.ToDays());
        Assert.DoesNotContain(DayOfWeek.Sunday, Weekdays.WeekdaysOnly.ToDays());
    }
}
