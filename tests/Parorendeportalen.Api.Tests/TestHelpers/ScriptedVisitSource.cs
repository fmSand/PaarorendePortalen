using Parorendeportalen.Api.Integrations;

namespace Parorendeportalen.Api.Tests.TestHelpers;

/// <summary>
/// Serves scripted pages in order, and refuses a cursor carrying anything other
/// than the token its own previous page issued. Without that check a caller
/// that reuses one token, or drops it, still walks the whole script.
/// </summary>
internal sealed class ScriptedVisitSource(params Func<VisitSnapshotPage>[] responses) : IVisitSource
{
    private readonly List<VisitSourceCursor> _cursors = [];
    private string? _issuedToken;
    private int _served;
    private bool _asked;

    public SourceSystem SourceSystem => SourceSystem.Synthetic;

    public IReadOnlyList<VisitSourceCursor> Cursors => _cursors;

    public static VisitSnapshotPage LastPage(params VisitSnapshot[] snapshots) =>
        new(snapshots, null);

    public Task<VisitSnapshotPage> FetchVisitsChangedSinceAsync(
        VisitSourceCursor cursor,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        _cursors.Add(cursor);

        // The first ask may carry a token from a stored position, which a run
        // resuming after the page cap does. Every ask after that has to carry
        // what this source itself just issued.
        if (_asked && cursor.ContinuationToken != _issuedToken)
        {
            throw new InvalidOperationException(
                $"Asked with token '{cursor.ContinuationToken ?? "<none>"}', but this source last issued '{_issuedToken ?? "<none>"}'."
            );
        }

        _asked = true;

        // The last response repeats, so a script can stand for a source that
        // keeps returning the same data across runs.
        var page = responses[Math.Min(_served++, responses.Length - 1)]();
        _issuedToken = page.ContinuationToken;

        return Task.FromResult(page);
    }
}
