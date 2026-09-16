namespace Parorendeportalen.Api.Services;

public enum WriteOutcome
{
    Succeeded,

    NotFound,

    // Exists, and the caller may see it, but did not write it.
    NotAuthor,

    // Exists and has no author: a source owns it, and the portal does not edit what it did not write.
    SourceOwned,

    // Someone else changed the row since the caller read it.
    VersionConflict,
}

public sealed record WriteResult<TValue>(WriteOutcome Outcome, TValue? Value)
    where TValue : class
{
    public static WriteResult<TValue> Succeeded(TValue value) => new(WriteOutcome.Succeeded, value);

    public static WriteResult<TValue> Failed(WriteOutcome outcome) => new(outcome, null);
}
