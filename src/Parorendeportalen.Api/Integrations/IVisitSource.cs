namespace Parorendeportalen.Api.Integrations;

public interface IVisitSource
{
    SourceSystem SourceSystem { get; }

    Task<VisitSnapshotPage> FetchVisitsChangedSinceAsync(
        VisitSourceCursor cursor,
        CancellationToken cancellationToken
    );
}
