namespace Parorendeportalen.Api.Integrations.Sync;

public class SyncRun
{
    public int Id { get; set; }

    public SourceSystem SourceSystem { get; set; }

    public SyncResourceType ResourceType { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public SyncRunStatus Status { get; set; }

    public int Inserted { get; set; }

    public int Updated { get; set; }

    public int Unchanged { get; set; }

    // Snapshots naming a care recipient the portal does not hold.
    public int Unresolved { get; set; }

    // The page cap cut this run short. It succeeded and left a token behind,
    // so the next run continues from there.
    public bool Truncated { get; set; }

    public string? Error { get; set; }
}

public enum SyncRunStatus
{
    // Zero value on purpose: a row left behind by a crashed process reads as
    // unfinished rather than as a success nobody wrote.
    Running = 0,
    Succeeded = 1,
    Failed = 2,
}
