using Parorendeportalen.Api.Integrations;

namespace Parorendeportalen.Api.Tests.Integrations;

public class NationalIdentifierTests
{
    // Literal OIDs. typo in a constant fails here.
    [Theory]
    [InlineData("urn:oid:2.16.578.1.12.4.1.4.1")]
    [InlineData("urn:oid:2.16.578.1.12.4.1.4.2")]
    [InlineData("urn:oid:2.16.578.1.12.4.1.4.3")]
    public void Constructor_Accepts_TheNorwegianIdentifierSystems(string system)
    {
        var identifier = new NationalIdentifier(system, "13116900216");

        Assert.Equal(system, identifier.System);
        Assert.Equal("13116900216", identifier.Value);
    }

    [Fact]
    public void ConstantsMatch_TheRegisterTheyName()
    {
        Assert.Equal("urn:oid:2.16.578.1.12.4.1.4.1", NationalIdentifier.FodselsnummerSystem);
        Assert.Equal("urn:oid:2.16.578.1.12.4.1.4.2", NationalIdentifier.DNummerSystem);
        Assert.Equal("urn:oid:2.16.578.1.12.4.1.4.3", NationalIdentifier.FellesHjelpenummerSystem);
    }

    [Theory]
    [InlineData("https://kildesystem.example/patient-id")]
    [InlineData("urn:oid:2.16.578.1.12.4.1.4.4")]
    [InlineData("2.16.578.1.12.4.1.4.1")]
    public void Constructor_Rejects_ASystemOutsideTheKnownRegisters(string system)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new NationalIdentifier(system, "13116900216")
        );

        Assert.Equal("system", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Rejects_ABlankValue(string value)
    {
        Assert.Throws<ArgumentException>(() =>
            new NationalIdentifier(NationalIdentifier.FodselsnummerSystem, value)
        );
    }

    [Fact]
    public void ADefaultIdentifier_ReportsItselfAsUnspecified()
    {
        Assert.False(default(NationalIdentifier).IsSpecified);
    }

    [Fact]
    public void AConstructedIdentifier_ReportsItselfAsSpecified()
    {
        var identifier = new NationalIdentifier(
            NationalIdentifier.FodselsnummerSystem,
            "13116900216"
        );

        Assert.True(identifier.IsSpecified);
    }

    [Fact]
    public void TwoIdentifiers_WithTheSameSystemAndValue_AreEqual()
    {
        var first = new NationalIdentifier(NationalIdentifier.FodselsnummerSystem, "13116900216");
        var second = new NationalIdentifier(NationalIdentifier.FodselsnummerSystem, "13116900216");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void TheSameValue_InDifferentRegisters_IsNotTheSameIdentifier()
    {
        var fodselsnummer = new NationalIdentifier(
            NationalIdentifier.FodselsnummerSystem,
            "13116900216"
        );
        var dNummer = new NationalIdentifier(NationalIdentifier.DNummerSystem, "13116900216");

        Assert.NotEqual(fodselsnummer, dNummer);
    }

    // Literal rather than composed from the constant, so a changed OID also fails here.
    [Fact]
    public void HashInput_PinsTheFormat_ToSystemPipeValue()
    {
        var identifier = new NationalIdentifier(
            NationalIdentifier.FodselsnummerSystem,
            "13116900216"
        );

        Assert.Equal("urn:oid:2.16.578.1.12.4.1.4.1|13116900216", identifier.HashInput);
    }

    [Fact]
    public void HashInput_DiffersBetweenRegisters_ForTheSameValue()
    {
        var fodselsnummer = new NationalIdentifier(
            NationalIdentifier.FodselsnummerSystem,
            "13116900216"
        );
        var dNummer = new NationalIdentifier(NationalIdentifier.DNummerSystem, "13116900216");

        Assert.NotEqual(fodselsnummer.HashInput, dNummer.HashInput);
    }

    [Fact]
    public void HashInput_OnAnUnspecifiedIdentifier_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => default(NationalIdentifier).HashInput);
    }

    [Fact]
    public void ToString_NamesTheRegister_WithoutTheValue()
    {
        var identifier = new NationalIdentifier(
            NationalIdentifier.FodselsnummerSystem,
            "13116900216"
        );

        Assert.Contains(NationalIdentifier.FodselsnummerSystem, identifier.ToString());
        Assert.DoesNotContain("13116900216", identifier.ToString());
    }
}
