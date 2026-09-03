using Parorendeportalen.Api.Integrations;

namespace Parorendeportalen.Api.Tests.TestHelpers;

/// <summary>
/// Blocks inside a fetch until the test releases it, then throws. It ignores
/// the cancellation token on purpose, so the worker can be mid-fetch when it is
/// asked to stop.
/// </summary>
internal sealed class GatedVisitSource : IVisitSource
{
    private readonly TaskCompletionSource _entered = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    private readonly TaskCompletionSource _released = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    public SourceSystem SourceSystem => SourceSystem.Synthetic;

    public Task Entered => _entered.Task;

    public void Release() => _released.TrySetResult();

    public async Task<VisitSnapshotPage> FetchVisitsChangedSinceAsync(
        VisitSourceCursor cursor,
        CancellationToken cancellationToken
    )
    {
        _entered.TrySetResult();
        await _released.Task;

        throw new HttpRequestException("the source is down");
    }
}
