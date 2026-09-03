using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Parorendeportalen.Api.Integrations;
using Parorendeportalen.Api.Integrations.Sync;

namespace Parorendeportalen.Api.Tests.Integrations.Sync;

// Kept out of the Postgres collection: these never reach a database, and a
// throwaway one per case costs more than the assertions are worth.
public class VisitSyncWorkerGuardTests
{
    private static VisitSyncWorker WorkerWith(VisitSyncOptions options) =>
        new(
            Substitute.For<IVisitSource>(),
            Substitute.For<IServiceScopeFactory>(),
            TimeProvider.System,
            options,
            NullLogger<VisitSyncWorker>.Instance
        );

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void APollIntervalThatIsNotPositive_IsRejected(int minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorkerWith(new VisitSyncOptions { PollInterval = TimeSpan.FromMinutes(minutes) })
        );
    }

    // Task.Delay throws above this, and the loop awaits it outside its own try,
    // so a typo in configuration would otherwise stop the host on tick one.
    [Fact]
    public void APollIntervalTaskDelayCannotHold_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorkerWith(
                new VisitSyncOptions
                {
                    PollInterval = VisitSyncOptions.MaxPollInterval + TimeSpan.FromMilliseconds(1),
                }
            )
        );
    }

    [Fact]
    public void ThePollIntervalAtThatBound_IsAccepted()
    {
        Assert.NotNull(
            WorkerWith(new VisitSyncOptions { PollInterval = VisitSyncOptions.MaxPollInterval })
        );
    }

    [Fact]
    public void ABackoffMultiplierBelowOne_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorkerWith(new VisitSyncOptions { MaxBackoffMultiplier = 0 })
        );
    }

    [Fact]
    public void TheDefaults_AreAccepted()
    {
        Assert.NotNull(WorkerWith(new VisitSyncOptions()));
    }
}
