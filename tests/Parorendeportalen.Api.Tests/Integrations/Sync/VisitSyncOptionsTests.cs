using Parorendeportalen.Api.Integrations.Sync;

namespace Parorendeportalen.Api.Tests.Integrations.Sync;

public class VisitSyncOptionsTests
{
    private static readonly VisitSyncOptions Options = new()
    {
        PollInterval = TimeSpan.FromMinutes(15),
        MaxBackoffMultiplier = 8,
    };

    [Fact]
    public void AfterASuccess_TheNextTickIsOnePollIntervalAway()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), Options.DelayAfter(consecutiveFailures: 0));
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    public void EachConsecutiveFailure_DoublesTheWait(int failures, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), Options.DelayAfter(failures));
    }

    // 31 and 63 are the failure counts where an unclamped 1 << n turns negative
    // and overflows the multiplication. The throw would escape the worker's
    // loop and stop the host, so a source down for a weekend must land here.
    [Theory]
    [InlineData(4)]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(50)]
    [InlineData(63)]
    [InlineData(int.MaxValue)]
    public void TheBackoff_StopsGrowingAtTheConfiguredMultiplier(int failures)
    {
        Assert.Equal(TimeSpan.FromMinutes(120), Options.DelayAfter(failures));
    }

    // A multiplier large enough to overflow the poll interval is still a
    // configured value, and a configuration mistake must not stop the host.
    [Fact]
    public void AnAbsurdMultiplier_IsCappedRatherThanOverflowing()
    {
        var options = new VisitSyncOptions
        {
            PollInterval = TimeSpan.FromMinutes(15),
            MaxBackoffMultiplier = int.MaxValue,
        };

        var delay = options.DelayAfter(consecutiveFailures: 31);

        Assert.Equal(TimeSpan.FromDays(1), delay);
    }

    // Capping must never poll more often than the configuration asked for.
    [Fact]
    public void APollIntervalLongerThanTheCap_IsNeverShortened()
    {
        var options = new VisitSyncOptions
        {
            PollInterval = TimeSpan.FromDays(3),
            MaxBackoffMultiplier = 8,
        };

        Assert.Equal(TimeSpan.FromDays(3), options.DelayAfter(consecutiveFailures: 0));
        Assert.Equal(TimeSpan.FromDays(3), options.DelayAfter(consecutiveFailures: 5));
    }

    [Fact]
    public void AMultiplierOfOne_KeepsThePollIntervalFlat()
    {
        var options = new VisitSyncOptions
        {
            PollInterval = TimeSpan.FromMinutes(15),
            MaxBackoffMultiplier = 1,
        };

        Assert.Equal(TimeSpan.FromMinutes(15), options.DelayAfter(consecutiveFailures: 4));
    }

    [Fact]
    public void ANegativeFailureCount_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Options.DelayAfter(-1));
    }

    [Fact]
    public void TheDefaults_PollAtLeastHourly()
    {
        var defaults = new VisitSyncOptions();

        Assert.True(defaults.Enabled);
        Assert.True(defaults.PollInterval <= TimeSpan.FromHours(1));
    }
}
