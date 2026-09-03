namespace Parorendeportalen.Api.Integrations.Sync;

public class SyncWatermark
{
    public int Id { get; set; }

    public SourceSystem SourceSystem { get; set; }

    public SyncResourceType ResourceType { get; set; }

    // Inclusive: the next fetch asks for SourceUpdatedAt >= this, because two
    // visits can share a timestamp and '>' would drop the tie.
    public DateTimeOffset? SourceUpdatedThrough { get; set; }

    // Set when a run stopped at the page cap. Without it the next run would
    // restart at the top of the batch it was midway through and never reach
    // the tail of a batch larger than one run can read.
    public string? ContinuationToken { get; set; }

    // The oldest snapshot a run could not place. A run the page cap cut short
    // leaves a token that starts the next one past it, so it has to be stored.
    public DateTimeOffset? UnresolvedFrom { get; set; }
}
