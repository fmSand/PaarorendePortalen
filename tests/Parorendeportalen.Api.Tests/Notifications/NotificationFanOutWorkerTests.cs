using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Notifications;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Notifications;

[Collection(PostgresCollection.Name)]
public class NotificationFanOutWorkerTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = Snapshots.Noon;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly CapturingLogger<NotificationFanOutWorker> _logger = new();
    private PostgresTestDatabase _factory = null!;
    private ServiceProvider? _services;
    private int _fridaId;
    private int _vigdisId;

    public async Task InitializeAsync()
    {
        _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

        using var context = _factory.CreateContext();
        var frida = new NextOfKin { NationalIdHash = "hash-frida", DisplayName = "Frida Sand" };
        var vigdis = new CareRecipient { Name = "Vigdis Quist" };
        context.AddRange(frida, vigdis);
        await context.SaveChangesAsync();

        _fridaId = frida.Id;
        _vigdisId = vigdis.Id;
    }

    public async Task DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        await _factory.DisposeAsync();
    }

    private static NotificationOptions Options(int batchSize = 100) =>
        new() { PollInterval = TimeSpan.FromMilliseconds(100), BatchSize = batchSize };

    private ServiceProvider RealServices(NotificationOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(builder =>
            builder.UseNpgsql(_factory.ConnectionString)
        );
        services.AddScoped<IChangeEventStore, EfChangeEventStore>();
        services.AddScoped<IConsentRepository, EfConsentRepository>();
        services.AddScoped<INotificationPreferenceRepository, EfNotificationPreferenceRepository>();
        services.AddScoped<INotificationFanOut, NotificationFanOut>();
        services.AddSingleton(options);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));

        return _services = services.BuildServiceProvider();
    }

    private ServiceProvider StubbedServices(INotificationFanOut fanOut)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => fanOut);

        return _services = services.BuildServiceProvider();
    }

    private NotificationFanOutWorker WorkerOver(
        ServiceProvider services,
        NotificationOptions options
    ) =>
        new(
            services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            options,
            _logger
        );

    private async Task EntitleAndChangeAsync()
    {
        using var context = _factory.CreateContext();
        context.KinshipGrants.Add(
            new KinshipGrant
            {
                NextOfKinId = _fridaId,
                CareRecipientId = _vigdisId,
                ValidFrom = Now.AddDays(-1),
            }
        );
        context.Consents.Add(
            new Consent
            {
                NextOfKinId = _fridaId,
                CareRecipientId = _vigdisId,
                Category = DataCategory.Visits,
                ValidFrom = Now.AddDays(-1),
            }
        );
        context.ChangeEvents.Add(
            new ChangeEvent
            {
                CareRecipientId = _vigdisId,
                Category = DataCategory.Visits,
                Kind = ChangeKind.Completed,
                ScheduledAt = Now.AddHours(-3),
                OccurredAt = Now.AddHours(-1),
            }
        );
        await context.SaveChangesAsync();
    }

    private async Task<List<Notification>> WaitForNotificationsAsync(int count)
    {
        var deadline = DateTime.UtcNow + Timeout;

        while (DateTime.UtcNow < deadline)
        {
            using var context = _factory.CreateContext();
            var rows = await context.Notifications.AsNoTracking().ToListAsync();
            if (rows.Count >= count)
            {
                return rows;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException(
            $"Fewer than {count} notifications within {Timeout}. Errors logged: "
                + (_logger.Errors.Count == 0 ? "none" : string.Join(" | ", _logger.Errors))
        );
    }

    [Fact]
    public async Task AFirstTick_DeliversWhatIsPending()
    {
        await EntitleAndChangeAsync();
        var options = Options();
        var worker = WorkerOver(RealServices(options), options);

        await worker.StartAsync(CancellationToken.None);
        var notification = Assert.Single(await WaitForNotificationsAsync(1));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(_fridaId, notification.NextOfKinId);
        Assert.Equal(ChangeKind.Completed, notification.Kind);

        using var context = _factory.CreateContext();
        Assert.Equal(Now, (await context.ChangeEvents.SingleAsync()).ProcessedAt);
    }

    [Fact]
    public async Task AFailingTick_IsLogged_AndTheWorkerKeepsTicking()
    {
        var fanOut = Substitute.For<INotificationFanOut>();
        var calls = 0;
        var secondCall = new TaskCompletionSource();
        fanOut
            .DeliverPendingAsync(Arg.Any<CancellationToken>())
            .Returns<FanOutResult>(_ =>
            {
                if (Interlocked.Increment(ref calls) >= 2)
                {
                    secondCall.TrySetResult();
                }

                throw new InvalidOperationException("the database is down");
            });
        var options = Options();
        var worker = WorkerOver(StubbedServices(fanOut), options);

        await worker.StartAsync(CancellationToken.None);
        await secondCall.Task.WaitAsync(Timeout);

        Assert.False(worker.ExecuteTask!.IsCompleted);
        await worker.StopAsync(CancellationToken.None);

        Assert.Contains(
            _logger.Errors,
            error => error.Contains("the database is down", StringComparison.Ordinal)
        );
    }

    // Sleeping between full batches would drain a backlog at BatchSize per poll interval.
    [Fact]
    public async Task AFullBatch_IsFollowedStraightAwayByAnotherTick()
    {
        var fanOut = Substitute.For<INotificationFanOut>();
        var calls = 0;
        var secondCall = new TaskCompletionSource();
        fanOut
            .DeliverPendingAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var call = Interlocked.Increment(ref calls);
                if (call >= 2)
                {
                    secondCall.TrySetResult();
                }

                return new FanOutResult(call == 1 ? 5 : 0, 0);
            });
        var options = new NotificationOptions
        {
            PollInterval = TimeSpan.FromHours(1),
            BatchSize = 5,
        };
        var worker = WorkerOver(StubbedServices(fanOut), options);

        await worker.StartAsync(CancellationToken.None);
        await secondCall.Task.WaitAsync(Timeout);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ATickThatCancelsOnStop_EndsTheWorkerCleanly()
    {
        var fanOut = Substitute.For<INotificationFanOut>();
        fanOut
            .DeliverPendingAsync(Arg.Any<CancellationToken>())
            .Returns<Task<FanOutResult>>(call => UntilCancelled(call.Arg<CancellationToken>()));
        var options = Options();
        var worker = WorkerOver(StubbedServices(fanOut), options);

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(worker.ExecuteTask!.IsCompleted);
        Assert.Empty(_logger.Errors);
    }

    private static async Task<FanOutResult> UntilCancelled(CancellationToken cancellationToken)
    {
        await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
        return new FanOutResult(0, 0);
    }
}
