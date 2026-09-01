using Parorendeportalen.Api.Integrations;
using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Tests.Integrations;

public class VisitSnapshotTests
{
    private static readonly NationalIdentifier Vigdis = new(
        NationalIdentifier.FodselsnummerSystem,
        "13116900216"
    );

    private static VisitSnapshot Valid() =>
        new()
        {
            SourceSystem = SourceSystem.Synthetic,
            ExternalId = "visit-0001",
            SourceUpdatedAt = new DateTimeOffset(2026, 9, 1, 6, 30, 0, TimeSpan.Zero),
            CareRecipient = Vigdis,
            ScheduledAt = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero),
            Status = VisitStatus.Planned,
        };

    [Fact]
    public void ASnapshot_CarriesTheProvenanceTriple_AndTheVisitPayload()
    {
        var snapshot = Valid() with
        {
            ActualAt = new DateTimeOffset(2026, 9, 1, 8, 12, 0, TimeSpan.Zero),
            Status = VisitStatus.Completed,
            CaregiverName = "Hjemmetjenesten Oslo",
            Notes = "Morgenstell.",
        };

        Assert.Equal(SourceSystem.Synthetic, snapshot.SourceSystem);
        Assert.Equal("visit-0001", snapshot.ExternalId);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 1, 6, 30, 0, TimeSpan.Zero),
            snapshot.SourceUpdatedAt
        );
        Assert.Equal(Vigdis, snapshot.CareRecipient);
        Assert.Equal(VisitStatus.Completed, snapshot.Status);
        Assert.Equal("Hjemmetjenesten Oslo", snapshot.CaregiverName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ExternalId_Rejects_ABlankValue(string externalId)
    {
        Assert.Throws<ArgumentException>(() => Valid() with { ExternalId = externalId });
    }

    [Fact]
    public void SourceSystem_Rejects_TheUnnamedZeroValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Valid() with { SourceSystem = default });
    }

    [Fact]
    public void CareRecipient_Rejects_AnUnspecifiedIdentifier()
    {
        Assert.Throws<ArgumentException>(() => Valid() with { CareRecipient = default });
    }

    // Guards the seam: a key of ours here would mean the source resolved it.
    [Fact]
    public void ASnapshot_ExposesNoKeyOfOurs()
    {
        var properties = typeof(VisitSnapshot).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain("Id", properties);
        Assert.DoesNotContain("CareRecipientId", properties);
        Assert.DoesNotContain("Origin", properties);
    }
}
