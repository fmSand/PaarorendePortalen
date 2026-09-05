using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Parorendeportalen.Api.Notifications;

namespace Parorendeportalen.Api.Tests.Notifications;

public class NotificationFanOutWorkerGuardTests
{
    private static NotificationFanOutWorker WorkerWith(NotificationOptions options) =>
        new(
            Substitute.For<IServiceScopeFactory>(),
            TimeProvider.System,
            options,
            NullLogger<NotificationFanOutWorker>.Instance
        );

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void APollIntervalThatIsNotPositive_IsRejected(int minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorkerWith(new NotificationOptions { PollInterval = TimeSpan.FromMinutes(minutes) })
        );
    }

    [Fact]
    public void APollIntervalTaskDelayCannotHold_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorkerWith(
                new NotificationOptions
                {
                    PollInterval =
                        NotificationOptions.MaxPollInterval + TimeSpan.FromMilliseconds(1),
                }
            )
        );
    }

    [Fact]
    public void ABatchSizeBelowOne_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorkerWith(new NotificationOptions { BatchSize = 0 })
        );
    }

    [Fact]
    public void TheDefaults_AreAccepted()
    {
        Assert.NotNull(WorkerWith(new NotificationOptions()));
    }
}
