namespace Parorendeportalen.Api.Notifications;

// The outbox consumer, assuming one instance. Two would take the same rows,
// and the unique index on (ChangeEventId, NextOfKinId) is what stops the loser
// delivering twice.
public sealed class NotificationFanOutWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationFanOutWorker> _logger;

    public NotificationFanOutWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        NotificationOptions options,
        ILogger<NotificationFanOutWorker> logger
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.PollInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            options.PollInterval,
            NotificationOptions.MaxPollInterval
        );
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BatchSize, 1);

        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var drained = await TickAsync(stoppingToken);

            try
            {
                await Task.Delay(
                    drained ? _options.PollInterval : TimeSpan.Zero,
                    _timeProvider,
                    stoppingToken
                );
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // A failed tick leaves the events unprocessed, and the next tick takes
    // them again. Same cancellation filter as the sync worker, for the same
    // reason: a timeout inside the tick must not stop the host.
    private async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        try
        {
            FanOutResult result;

            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                result = await scope
                    .ServiceProvider.GetRequiredService<INotificationFanOut>()
                    .DeliverPendingAsync(cancellationToken);
            }

            if (result.Processed > 0)
            {
                _logger.LogInformation(
                    "Fan-out took {Processed} change events and delivered {Delivered} notifications.",
                    result.Processed,
                    result.Delivered
                );
            }

            return result.Processed < _options.BatchSize;
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested
            )
        {
            _logger.LogError(
                exception,
                "Notification fan-out failed. The change events stay unprocessed for the next tick."
            );
            return true;
        }
    }
}
