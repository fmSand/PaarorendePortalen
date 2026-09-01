using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Integrations;

// Half of the sync watermark key. Origin also covers portal-authored rows.
public enum SourceSystem
{
    // No zero value, so an unset source fails the mapping below.
    Synthetic = 1
}

public static class SourceSystemExtensions
{
    // Never Origin.Portal, so ingestion cannot reconcile away an authored row.
    public static Origin ToOrigin(this SourceSystem sourceSystem) => sourceSystem switch
    {
        SourceSystem.Synthetic => Origin.Synthetic,
        _ => throw new ArgumentOutOfRangeException(
            nameof(sourceSystem),
            sourceSystem,
            "No Origin is mapped for this source system.")
    };
}
