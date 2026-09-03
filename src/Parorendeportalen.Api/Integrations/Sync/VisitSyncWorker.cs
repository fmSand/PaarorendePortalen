namespace Parorendeportalen.Api.Integrations.Sync;

// One worker per source, per the integration design. A single job over every
// source would give a source that is down the power to delay every other
// source's data.
public sealed class VisitSyncWorker : BackgroundService
{
    // A compensating write, so it does not ride the token that may be why the
    // run failed.
    private static readonly TimeSpan RecordFailureTimeout = TimeSpan.FromSeconds(10);

    private readonly IVisitSource _source;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly VisitSyncOptions _options;
    private readonly ILogger<VisitSyncWorker> _logger;

    public VisitSyncWorker(
        IVisitSource source,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        VisitSyncOptions options,
        ILogger<VisitSyncWorker> logger
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.PollInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            options.PollInterval,
            VisitSyncOptions.MaxPollInterval
        );
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxBackoffMultiplier, 1);

        _source = source;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    private enum TickOutcome
    {
        Drained,
        MoreToRead,
        Failed,
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var outcome = await TickAsync(stoppingToken);
            consecutiveFailures = outcome == TickOutcome.Failed ? consecutiveFailures + 1 : 0;

            // A backlog the page cap cut short drains as fast as the source
            // serves it.
            var delay =
                outcome == TickOutcome.MoreToRead
                    ? TimeSpan.Zero
                    : _options.DelayAfter(consecutiveFailures);

            try
            {
                await Task.Delay(delay, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // Each tick is its own try/catch: a source that is down must not take the
    // host down with it. Each step gets a fresh scope, because a run that
    // failed leaves its writes in the DbContext and recording on that same
    // context would retry them.
    private async Task<TickOutcome> TickAsync(CancellationToken cancellationToken)
    {
        int runId;
        SyncPosition resumeFrom;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var state = scope.ServiceProvider.GetRequiredService<ISyncStateStore>();

            resumeFrom = await state.GetPositionAsync(
                _source.SourceSystem,
                SyncResourceType.Visit,
                cancellationToken
            );
            runId = await state.StartRunAsync(
                _source.SourceSystem,
                SyncResourceType.Visit,
                cancellationToken
            );
        }
        // A source with a timeout of its own throws TaskCanceledException,
        // which is an OperationCanceledException. Filtering on the type alone
        // leaves the run Running and stops the host on a source that is slow.
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested
            )
        {
            _logger.LogError(
                exception,
                "Could not open a sync run for {SourceSystem}.",
                _source.SourceSystem
            );
            return TickOutcome.Failed;
        }

        try
        {
            VisitSyncOutcome outcome;

            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                outcome = await scope
                    .ServiceProvider.GetRequiredService<IVisitSyncService>()
                    .RunAsync(_source, resumeFrom, cancellationToken);
            }

            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                await scope
                    .ServiceProvider.GetRequiredService<ISyncStateStore>()
                    .CompleteRunAsync(runId, outcome, cancellationToken);
            }

            _logger.LogInformation(
                "Sync run {RunId} against {SourceSystem}: {Inserted} inserted, {Updated} updated, {Unchanged} unchanged, {Unresolved} unresolved, truncated {Truncated}.",
                runId,
                _source.SourceSystem,
                outcome.Ingestion.Inserted,
                outcome.Ingestion.Updated,
                outcome.Ingestion.Unchanged,
                outcome.UnresolvedSnapshots,
                outcome.Truncated
            );

            if (!outcome.Truncated)
            {
                return TickOutcome.Drained;
            }

            // A truncated run that stored the token it resumed from got
            // nowhere, and draining that as fast as the source serves it is a
            // busy loop.
            if (outcome.Position?.ContinuationToken == resumeFrom.ContinuationToken)
            {
                _logger.LogWarning(
                    "Sync of {SourceSystem} stopped at the page cap without moving off the position it resumed from.",
                    _source.SourceSystem
                );

                return TickOutcome.Drained;
            }

            return TickOutcome.MoreToRead;
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested
            )
        {
            _logger.LogError(
                exception,
                "Sync run {RunId} against {SourceSystem} failed.",
                runId,
                _source.SourceSystem
            );
            await RecordFailureAsync(runId, exception);
            return TickOutcome.Failed;
        }
    }

    private async Task RecordFailureAsync(int runId, Exception exception)
    {
        try
        {
            using var timeout = new CancellationTokenSource(RecordFailureTimeout, _timeProvider);
            await using var scope = _scopeFactory.CreateAsyncScope();

            await scope
                .ServiceProvider.GetRequiredService<ISyncStateStore>()
                .FailRunAsync(runId, exception.Message, timeout.Token);
        }
        catch (Exception recordingFailure)
        {
            _logger.LogError(
                recordingFailure,
                "Could not record the failure of sync run {RunId}.",
                runId
            );
        }
    }
}
