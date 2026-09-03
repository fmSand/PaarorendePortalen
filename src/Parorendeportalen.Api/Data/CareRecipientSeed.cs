using Parorendeportalen.Api.Integrations;

namespace Parorendeportalen.Api.Data;

public sealed record CareRecipientSeed(
    string Name,
    NationalIdentifier NationalIdentifier,
    string Key
);

// Read by the seeder and by the synthetic source, so the snapshots the source
// emits resolve against the rows the seeder wrote. Synthetic numbers from
// Skatteetaten's Tenor, held in user-secrets like the kinship seed grants.
public static class CareRecipientSeedReader
{
    public const string SectionName = "CareRecipients:Seed";

    public static IReadOnlyList<CareRecipientSeed> Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var seeds = new List<CareRecipientSeed>();

        foreach (var entry in configuration.GetSection(SectionName).GetChildren())
        {
            var name = entry["Name"];
            var nationalId = entry["NationalId"];

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(nationalId))
            {
                continue;
            }

            var system = entry["System"] ?? NationalIdentifier.FodselsnummerSystem;
            var key = entry["Key"];

            // The name is the fallback because the seed list already treats it
            // as what identifies an entry, and it survives a reordering.
            seeds.Add(
                new CareRecipientSeed(
                    name,
                    new NationalIdentifier(system, nationalId),
                    string.IsNullOrWhiteSpace(key) ? name : key
                )
            );
        }

        RejectDuplicates(seeds);

        return seeds;
    }

    // Two entries sharing a number collide on the filtered unique index partway
    // through starting the host; two sharing a key hand the synthetic feed one
    // set of visit ids for two people.
    private static void RejectDuplicates(List<CareRecipientSeed> seeds)
    {
        var repeatedKey = seeds
            .GroupBy(seed => seed.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (repeatedKey is not null)
        {
            throw new InvalidOperationException(
                $"'{SectionName}' gives {Names(repeatedKey)} the same feed key '{repeatedKey.Key}'. Set a distinct Key on one of them."
            );
        }

        var repeatedNumber = seeds
            .GroupBy(seed => seed.NationalIdentifier.HashInput, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (repeatedNumber is not null)
        {
            throw new InvalidOperationException(
                $"'{SectionName}' gives {Names(repeatedNumber)} the same national identifier."
            );
        }
    }

    private static string Names(IEnumerable<CareRecipientSeed> seeds) =>
        string.Join(" and ", seeds.Select(seed => $"'{seed.Name}'"));
}
