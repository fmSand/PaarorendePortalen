using System.Globalization;

namespace Parorendeportalen.Api.Integrations.Synthetic;

// The token this source issues. It carries the whole sort key, so a page can
// resume inside a run of visits that share a SourceUpdatedAt.
internal readonly record struct SyntheticPagePosition(
    DateTimeOffset SourceUpdatedAt,
    string ExternalId
)
{
    private const char Separator = '|';

    public static SyntheticPagePosition From(VisitSnapshot snapshot) =>
        new(snapshot.SourceUpdatedAt, snapshot.ExternalId);

    public static SyntheticPagePosition? Parse(string? token)
    {
        if (token is null)
        {
            return null;
        }

        // Null for a token it can't read. Tokens outlive the process in SyncWatermarks,
        // and throwing on an old format would fail every tick until someone cleared it.
        var separator = token.IndexOf(Separator, StringComparison.Ordinal);
        if (
            separator <= 0
            || separator == token.Length - 1
            || !DateTimeOffset.TryParseExact(
                token[..separator],
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var sourceUpdatedAt
            )
        )
        {
            return null;
        }

        return new SyntheticPagePosition(sourceUpdatedAt, token[(separator + 1)..]);
    }

    public bool Precedes(VisitSnapshot snapshot) =>
        snapshot.SourceUpdatedAt > SourceUpdatedAt
        || (
            snapshot.SourceUpdatedAt == SourceUpdatedAt
            && string.CompareOrdinal(snapshot.ExternalId, ExternalId) > 0
        );

    public string ToToken() =>
        string.Create(CultureInfo.InvariantCulture, $"{SourceUpdatedAt:O}{Separator}{ExternalId}");
}
