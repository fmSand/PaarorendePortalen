using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Tests.Models;

public class RecurrenceRuleTests
{
    [Fact]
    public void Days_None_IsRejected()
    {
        // Would otherwise read as "nothing today" every day, indistinguishable from a correct empty plan.
        var thrown = Assert.Throws<ArgumentException>(() =>
            new RecurrenceRule { Days = Weekdays.None, TimesPerDay = 1 }
        );

        Assert.Contains("at least one weekday", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Days_BitsOutsideTheWeek_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RecurrenceRule { Days = (Weekdays)128, TimesPerDay = 1 }
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(RecurrenceRule.MaxTimesPerDay + 1)]
    public void TimesPerDay_OutsideTheAllowedRange_IsRejected(int timesPerDay)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RecurrenceRule { Days = Weekdays.Monday, TimesPerDay = timesPerDay }
        );
    }

    [Theory]
    [InlineData(1)]
    [InlineData(RecurrenceRule.MaxTimesPerDay)]
    public void TimesPerDay_AtTheEdgesOfTheAllowedRange_IsAccepted(int timesPerDay)
    {
        var rule = new RecurrenceRule { Days = Weekdays.Monday, TimesPerDay = timesPerDay };

        Assert.Equal(timesPerDay, rule.TimesPerDay);
    }

    [Fact]
    public void Covers_WeekdaysOnlyRule_ExcludesTheWeekend()
    {
        var rule = new RecurrenceRule { Days = Weekdays.WeekdaysOnly, TimesPerDay = 2 };

        // 2026-09-04 is a Friday, so the 5th and 6th are the weekend.
        Assert.True(rule.Covers(new DateOnly(2026, 9, 4)));
        Assert.False(rule.Covers(new DateOnly(2026, 9, 5)));
        Assert.False(rule.Covers(new DateOnly(2026, 9, 6)));
        Assert.True(rule.Covers(new DateOnly(2026, 9, 7)));
    }

    [Fact]
    public void Covers_SingleDayRule_MatchesOnlyThatDay()
    {
        var rule = new RecurrenceRule { Days = Weekdays.Wednesday, TimesPerDay = 1 };

        // 2026-09-02 is a Wednesday, and so is the 9th a week later.
        Assert.True(rule.Covers(new DateOnly(2026, 9, 2)));
        Assert.True(rule.Covers(new DateOnly(2026, 9, 9)));
        Assert.False(rule.Covers(new DateOnly(2026, 9, 3)));
    }
}
