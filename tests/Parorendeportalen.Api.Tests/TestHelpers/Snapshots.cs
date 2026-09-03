using Parorendeportalen.Api.Integrations;
using Parorendeportalen.Api.Integrations.Synthetic;
using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Tests.TestHelpers;

internal static class Snapshots
{
    public static readonly NationalIdentifier Vigdis = new(
        NationalIdentifier.FodselsnummerSystem,
        "13116900216"
    );

    public static readonly NationalIdentifier Tor = new(
        NationalIdentifier.FodselsnummerSystem,
        "29099900157"
    );

    // A third person, so a test can add a seed entry without reusing the two
    // above.
    public static readonly NationalIdentifier Kari = new(
        NationalIdentifier.DNummerSystem,
        "53116900216"
    );

    // Keyed on the display name, which is what the seed reader falls back to,
    // so the feed's ExternalIds carry a space the way they do by default.
    public static readonly SyntheticRecipient VigdisRecipient = new("Vigdis Quist", Vigdis);

    public static readonly SyntheticRecipient TorRecipient = new("Tor Quist", Tor);

    public static readonly SyntheticRecipient KariRecipient = new("Kari Nordmann", Kari);

    // Both in UTC. Going through DateTimeOffset.Date would hand back a
    // DateTime that converts using the machine's own offset.
    public static readonly DateTimeOffset Midnight = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset Noon = Midnight.AddHours(12);

    // ScheduledAt defaults away from SourceUpdatedAt on purpose. Collapsing the
    // two would let a watermark computed from the wrong one still pass.
    public static VisitSnapshot Visit(
        string externalId,
        DateTimeOffset sourceUpdatedAt,
        NationalIdentifier? careRecipient = null,
        DateTimeOffset? scheduledAt = null,
        DateTimeOffset? actualAt = null,
        VisitStatus status = VisitStatus.Planned,
        string? caregiverName = "Hjemmetjenesten Oslo",
        string? notes = null
    ) =>
        new()
        {
            SourceSystem = SourceSystem.Synthetic,
            ExternalId = externalId,
            CareRecipient = careRecipient ?? Vigdis,
            SourceUpdatedAt = sourceUpdatedAt,
            ScheduledAt = scheduledAt ?? sourceUpdatedAt.AddHours(3),
            ActualAt = actualAt,
            Status = status,
            CaregiverName = caregiverName,
            Notes = notes,
        };
}
