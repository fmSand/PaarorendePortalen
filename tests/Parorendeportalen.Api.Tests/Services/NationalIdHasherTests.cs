using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Tests.Services;

public class NationalIdHasherTests
{
    private const string NationalId = "12345678901";
    private const string OtherNationalId = "10987654321";

    [Fact]
    public void Hash_SamePepperAndNationalId_ReturnsSameHashAcrossInstances()
    {
        var first = new NationalIdHasher("test-pepper");
        var second = new NationalIdHasher("test-pepper");

        Assert.Equal(first.Hash(NationalId), second.Hash(NationalId));
    }

    [Fact]
    public void Hash_CalledTwiceOnSameInstance_ReturnsSameHash()
    {
        var sut = new NationalIdHasher("test-pepper");

        Assert.Equal(sut.Hash(NationalId), sut.Hash(NationalId));
    }

    [Fact]
    public void Hash_DifferentPeppers_ReturnDifferentHashesForSameNationalId()
    {
        var withOnePepper = new NationalIdHasher("test-pepper");
        var withAnotherPepper = new NationalIdHasher("different-pepper");

        Assert.NotEqual(withOnePepper.Hash(NationalId), withAnotherPepper.Hash(NationalId));
    }

    [Fact]
    public void Hash_DifferentNationalIds_ReturnDifferentHashesForSamePepper()
    {
        var sut = new NationalIdHasher("test-pepper");

        Assert.NotEqual(sut.Hash(NationalId), sut.Hash(OtherNationalId));
    }

    [Fact]
    public void Hash_NationalIdsDifferingByOneCharacter_ReturnDifferentHashes()
    {
        var sut = new NationalIdHasher("test-pepper");

        Assert.NotEqual(sut.Hash("12345678901"), sut.Hash("12345678902"));
    }

    [Fact]
    public void Hash_AnyNationalId_DoesNotExposeRawNationalId()
    {
        var sut = new NationalIdHasher("test-pepper");

        var result = sut.Hash(NationalId);

        Assert.NotEqual(NationalId, result);
        Assert.DoesNotContain(NationalId, result);
    }

    [Fact]
    public void Hash_AnyNationalId_ReturnsSixtyFourUppercaseHexCharacters()
    {
        var sut = new NationalIdHasher("test-pepper");

        var result = sut.Hash(NationalId);

        Assert.Equal(64, result.Length);
        Assert.All(result, character => Assert.Contains(character, "0123456789ABCDEF"));
    }

    [Fact]
    public void Hash_KnownPepperAndNationalId_MatchesIndependentlyComputedHmacSha256()
    {
        var sut = new NationalIdHasher("test-pepper");

        var result = sut.Hash(NationalId);

        Assert.Equal("A0602840C114A4AEABD1EDD4671AF27A8D592FBB06A3CEE42DB94F1D9E6BFD6B", result);
    }

    [Fact]
    public void Hash_EmptyNationalId_StillReturnsPepperKeyedDigest()
    {
        var withOnePepper = new NationalIdHasher("test-pepper");
        var withAnotherPepper = new NationalIdHasher("different-pepper");

        var result = withOnePepper.Hash(string.Empty);

        Assert.Equal(64, result.Length);
        Assert.NotEqual(withAnotherPepper.Hash(string.Empty), result);
    }
}
