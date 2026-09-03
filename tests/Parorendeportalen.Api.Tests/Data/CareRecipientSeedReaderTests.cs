using Microsoft.Extensions.Configuration;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Integrations;

namespace Parorendeportalen.Api.Tests.Data;

public class CareRecipientSeedReaderTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                entries.Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value))
            )
            .Build();

    [Fact]
    public void AnEntry_BecomesAFodselsnummerByDefault()
    {
        var configuration = Configuration(
            ("CareRecipients:Seed:0:Name", "Vigdis Quist"),
            ("CareRecipients:Seed:0:NationalId", "13116900216")
        );

        var seed = Assert.Single(CareRecipientSeedReader.Read(configuration));

        Assert.Equal("Vigdis Quist", seed.Name);
        Assert.Equal(NationalIdentifier.FodselsnummerSystem, seed.NationalIdentifier.System);
        Assert.Equal("13116900216", seed.NationalIdentifier.Value);
    }

    [Fact]
    public void AnEntry_CanNameAnotherRegister()
    {
        var configuration = Configuration(
            ("CareRecipients:Seed:0:Name", "Vigdis Quist"),
            ("CareRecipients:Seed:0:NationalId", "53116900216"),
            ("CareRecipients:Seed:0:System", NationalIdentifier.DNummerSystem)
        );

        var seed = Assert.Single(CareRecipientSeedReader.Read(configuration));

        Assert.Equal(NationalIdentifier.DNummerSystem, seed.NationalIdentifier.System);
    }

    // A half-filled entry would otherwise seed a recipient sync can never find.
    [Theory]
    [InlineData("CareRecipients:Seed:0:Name", "Vigdis Quist")]
    [InlineData("CareRecipients:Seed:0:NationalId", "13116900216")]
    public void AnIncompleteEntry_IsSkipped(string key, string value)
    {
        Assert.Empty(CareRecipientSeedReader.Read(Configuration((key, value))));
    }

    [Fact]
    public void NoSection_MeansNoSeeds()
    {
        Assert.Empty(CareRecipientSeedReader.Read(Configuration()));
    }

    // The production case is two care recipients, so reading only the first
    // would leave the second with no number and her visits unresolved.
    [Fact]
    public void EveryEntry_IsRead()
    {
        var configuration = Configuration(
            ("CareRecipients:Seed:0:Name", "Vigdis Quist"),
            ("CareRecipients:Seed:0:NationalId", "13116900216"),
            ("CareRecipients:Seed:1:Name", "Tor Quist"),
            ("CareRecipients:Seed:1:NationalId", "29099900157")
        );

        var seeds = CareRecipientSeedReader.Read(configuration);

        Assert.Equal(["Vigdis Quist", "Tor Quist"], seeds.Select(seed => seed.Name));
        Assert.Equal(
            ["13116900216", "29099900157"],
            seeds.Select(seed => seed.NationalIdentifier.Value)
        );
    }

    // Skipped rather than passed on, where NationalIdentifier's own guard would
    // turn a stray space in user-secrets into a failure to start.
    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    public void AWhitespaceOnlyValue_IsSkipped(string blank)
    {
        var configuration = Configuration(
            ("CareRecipients:Seed:0:Name", "Vigdis Quist"),
            ("CareRecipients:Seed:0:NationalId", blank)
        );

        Assert.Empty(CareRecipientSeedReader.Read(configuration));
    }

    // The feed builds its ExternalIds from the key, so it has to name the same
    // person after the list is reordered. The entry's position does not.
    [Fact]
    public void AnEntryWithoutAKey_FallsBackToOneMadeFromTheName()
    {
        var configuration = Configuration(
            ("CareRecipients:Seed:0:Name", "Tor Quist"),
            ("CareRecipients:Seed:0:NationalId", "29099900157")
        );

        var reordered = Configuration(
            ("CareRecipients:Seed:0:Name", "Vigdis Quist"),
            ("CareRecipients:Seed:0:NationalId", "13116900216"),
            ("CareRecipients:Seed:1:Name", "Tor Quist"),
            ("CareRecipients:Seed:1:NationalId", "29099900157")
        );

        var key = Assert.Single(CareRecipientSeedReader.Read(configuration)).Key;

        Assert.Equal("Tor Quist", key);
        Assert.Equal(key, CareRecipientSeedReader.Read(reordered)[1].Key);
    }

    [Fact]
    public void AKeyInConfiguration_WinsOverTheOneMadeFromTheName()
    {
        var configuration = Configuration(
            ("CareRecipients:Seed:0:Name", "Vigdis Quist"),
            ("CareRecipients:Seed:0:NationalId", "13116900216"),
            ("CareRecipients:Seed:0:Key", "pasient-4711")
        );

        Assert.Equal(
            "pasient-4711",
            Assert.Single(CareRecipientSeedReader.Read(configuration)).Key
        );
    }

    // Two entries under one key give the feed one set of visit ids for two
    // people, and the upsert would swap the rows between them every run.
    [Fact]
    public void TwoEntriesSharingAKey_AreRejected_NamingBoth()
    {
        var configuration = Configuration(
            ("CareRecipients:Seed:0:Name", "Vigdis Quist"),
            ("CareRecipients:Seed:0:NationalId", "13116900216"),
            ("CareRecipients:Seed:1:Name", "Tor Quist"),
            ("CareRecipients:Seed:1:NationalId", "29099900157"),
            ("CareRecipients:Seed:1:Key", "Vigdis Quist")
        );

        var error = Assert.Throws<InvalidOperationException>(() =>
            CareRecipientSeedReader.Read(configuration)
        );

        Assert.Contains("Vigdis Quist", error.Message, StringComparison.Ordinal);
        Assert.Contains("Tor Quist", error.Message, StringComparison.Ordinal);
    }

    // Both hash the same, so the filtered unique index refuses the second one
    // as a DbUpdateException partway through starting the host.
    [Fact]
    public void TwoEntriesSharingANumber_AreRejected_WithoutRepeatingIt()
    {
        var configuration = Configuration(
            ("CareRecipients:Seed:0:Name", "Vigdis Quist"),
            ("CareRecipients:Seed:0:NationalId", "13116900216"),
            ("CareRecipients:Seed:1:Name", "Vigdis Kvist"),
            ("CareRecipients:Seed:1:NationalId", "13116900216")
        );

        var error = Assert.Throws<InvalidOperationException>(() =>
            CareRecipientSeedReader.Read(configuration)
        );

        Assert.Contains("Vigdis Kvist", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("13116900216", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASystemOutsideTheKnownRegisters_Throws()
    {
        var configuration = Configuration(
            ("CareRecipients:Seed:0:Name", "Vigdis Quist"),
            ("CareRecipients:Seed:0:NationalId", "13116900216"),
            ("CareRecipients:Seed:0:System", "https://kildesystem.example/patient-id")
        );

        Assert.Throws<ArgumentException>(() => CareRecipientSeedReader.Read(configuration));
    }
}
