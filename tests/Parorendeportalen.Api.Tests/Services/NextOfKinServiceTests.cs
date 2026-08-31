using NSubstitute;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Tests.Services;

public class NextOfKinServiceTests
{
    private readonly IKinshipRegistry _registry = Substitute.For<IKinshipRegistry>();
    private readonly NationalIdHasher _hasher = new("test-pepper");
    private readonly NextOfKinService _sut;

    public NextOfKinServiceTests()
    {
        _sut = new NextOfKinService(_registry, _hasher);
    }

    private static NextOfKin CreatePerson(
        int id = 1,
        string? externalId = "sub-123",
        string nationalIdHash = "hash-1",
        string displayName = "Frida Sand",
        params int[] careRecipientIds)
    {
        var person = new NextOfKin
        {
            Id = id,
            ExternalId = externalId,
            NationalIdHash = nationalIdHash,
            DisplayName = displayName
        };

        person.Grants.AddRange(careRecipientIds.Select(careRecipientId => new KinshipGrant
        {
            NextOfKinId = id,
            CareRecipientId = careRecipientId,
            CareRecipient = new CareRecipient { Id = careRecipientId, Name = $"Care recipient {careRecipientId}" }
        }));

        return person;
    }

    [Fact]
    public async Task GetByExternalIdAsync_ReturnsMappedNextOfKin_WhenFound()
    {
        _registry.GetByExternalIdAsync("sub-123", Arg.Any<CancellationToken>())
            .Returns(CreatePerson(careRecipientIds: 7));

        var result = await _sut.GetByExternalIdAsync("sub-123", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("Frida Sand", result.DisplayName);
        var grant = Assert.Single(result.Grants);
        Assert.Equal(7, grant.CareRecipientId);
    }

    [Fact]
    public async Task GetByExternalIdAsync_ReturnsEveryGrant_WhenCallerHoldsSeveral()
    {
        _registry.GetByExternalIdAsync("sub-123", Arg.Any<CancellationToken>())
            .Returns(CreatePerson(careRecipientIds: [1, 2]));

        var result = await _sut.GetByExternalIdAsync("sub-123", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal([1, 2], result.Grants.Select(g => g.CareRecipientId));
    }

    [Fact]
    public async Task GetByExternalIdAsync_ReturnsNull_WhenNotFound()
    {
        _registry.GetByExternalIdAsync("unknown-sub", Arg.Any<CancellationToken>())
            .Returns((NextOfKin?)null);

        var result = await _sut.GetByExternalIdAsync("unknown-sub", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCareRecipientIdsByExternalIdAsync_ReturnsEveryGrantedId()
    {
        _registry.GetByExternalIdAsync("sub-123", Arg.Any<CancellationToken>())
            .Returns(CreatePerson(careRecipientIds: [7, 9]));

        var result = await _sut.GetCareRecipientIdsByExternalIdAsync("sub-123", CancellationToken.None);

        Assert.Equal([7, 9], result);
    }

    [Fact]
    public async Task GetCareRecipientIdsByExternalIdAsync_ReturnsEmpty_WhenNotFound()
    {
        _registry.GetByExternalIdAsync("unknown-sub", Arg.Any<CancellationToken>())
            .Returns((NextOfKin?)null);

        var result = await _sut.GetCareRecipientIdsByExternalIdAsync("unknown-sub", CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ResolveOrBindAsync_ReturnsExisting_WhenExternalIdAlreadyBound_AndDisplayNameUnchanged()
    {
        _registry.GetByExternalIdAsync("sub-123", Arg.Any<CancellationToken>())
            .Returns(CreatePerson(careRecipientIds: 7));

        var result = await _sut.ResolveOrBindAsync("sub-123", "12345678901", "Frida Sand", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal(7, Assert.Single(result.Grants).CareRecipientId);

        await _registry.DidNotReceive().UpdateAsync(Arg.Any<NextOfKin>(), Arg.Any<CancellationToken>());
        await _registry.DidNotReceive().GetByNationalIdHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveOrBindAsync_RefreshesDisplayName_WhenExternalIdBound_ButNameChanged()
    {
        _registry.GetByExternalIdAsync("sub-123", Arg.Any<CancellationToken>())
            .Returns(CreatePerson(displayName: "Old Name", careRecipientIds: 7));

        var result = await _sut.ResolveOrBindAsync("sub-123", "12345678901", "New Name", CancellationToken.None);

        Assert.Equal("New Name", result!.DisplayName);
        await _registry.Received(1).UpdateAsync(
            Arg.Is<NextOfKin>(n => n.DisplayName == "New Name" && n.Id == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveOrBindAsync_BindsSeededGrant_OnFirstMatchByNationalIdHash()
    {
        _registry.GetByExternalIdAsync("new-sub-456", Arg.Any<CancellationToken>())
            .Returns((NextOfKin?)null);

        var expectedHash = _hasher.Hash("12345678901");
        _registry.GetByNationalIdHashAsync(expectedHash, Arg.Any<CancellationToken>())
            .Returns(CreatePerson(
                id: 3, externalId: null, nationalIdHash: expectedHash,
                displayName: "Test Testen", careRecipientIds: 1));

        var result = await _sut.ResolveOrBindAsync("new-sub-456", "12345678901", "Test Testen", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(3, result.Id);
        Assert.Equal(1, Assert.Single(result.Grants).CareRecipientId);

        await _registry.Received(1).UpdateAsync(
            Arg.Is<NextOfKin>(n => n.ExternalId == "new-sub-456" && n.Id == 3), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveOrBindAsync_ReturnsNull_WhenNoGrantExistsForEitherLookup()
    {
        _registry.GetByExternalIdAsync("stranger-sub", Arg.Any<CancellationToken>())
            .Returns((NextOfKin?)null);
        _registry.GetByNationalIdHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((NextOfKin?)null);

        var result = await _sut.ResolveOrBindAsync("stranger-sub", "00000000000", "Stranger", CancellationToken.None);

        Assert.Null(result);
        await _registry.DidNotReceive().UpdateAsync(Arg.Any<NextOfKin>(), Arg.Any<CancellationToken>());
    }

    // A person row can outlive its grants - identity alone must not authorise
    [Fact]
    public async Task ResolveOrBindAsync_ReturnsNull_WhenPersonExistsButHoldsNoCurrentGrant()
    {
        _registry.GetByExternalIdAsync("expired-sub", Arg.Any<CancellationToken>())
            .Returns(CreatePerson(externalId: "expired-sub"));

        var result = await _sut.ResolveOrBindAsync("expired-sub", "12345678901", "Frida Sand", CancellationToken.None);

        Assert.Null(result);
    }
}
