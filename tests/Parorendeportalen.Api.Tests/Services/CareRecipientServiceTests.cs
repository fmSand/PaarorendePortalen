using NSubstitute;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Tests.Services;

public class CareRecipientServiceTests
{
    private readonly ICareRecipientRepository _repository = Substitute.For<ICareRecipientRepository>();
    private readonly CareRecipientService _sut;

    public CareRecipientServiceTests()
    {
        _sut = new CareRecipientService(_repository);
    }

    [Fact]
    public async Task GetByIdsAsync_ReturnsMappedCareRecipients()
    {
        var careRecipients = new List<CareRecipient>
        {
            new() { Id = 1, Name = "Vigdis Quist" },
            new() { Id = 2, Name = "Tor Quist" }
        };
        _repository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(careRecipients);

        var result = await _sut.GetByIdsAsync([1, 2], CancellationToken.None);

        Assert.Collection(
            result,
            first =>
            {
                Assert.Equal(1, first.Id);
                Assert.Equal("Vigdis Quist", first.Name);
            },
            second =>
            {
                Assert.Equal(2, second.Id);
                Assert.Equal("Tor Quist", second.Name);
            });
    }

    [Fact]
    public async Task GetByIdsAsync_ReturnsEmptyList_WhenRepositoryHasNone()
    {
        _repository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new List<CareRecipient>());

        var result = await _sut.GetByIdsAsync([1], CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByIdsAsync_PassesTheRequestedIdsThroughUnchanged()
    {
        _repository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new List<CareRecipient>());

        await _sut.GetByIdsAsync([4, 9], CancellationToken.None);

        await _repository.Received(1).GetByIdsAsync(
            Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { 4, 9 })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsMappedCareRecipient_WhenFound()
    {
        _repository.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new CareRecipient { Id = 1, Name = "Kari Nordmann" });

        var result = await _sut.GetByIdAsync(1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("Kari Nordmann", result.Name);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenRepositoryReturnsNull()
    {
        _repository.GetByIdAsync(999, Arg.Any<CancellationToken>())
            .Returns((CareRecipient?)null);

        var result = await _sut.GetByIdAsync(999, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_PassesRequestedIdToRepository_AndDoesNotFallBackToAnotherRecord()
    {
        _repository.GetByIdAsync(3, Arg.Any<CancellationToken>())
            .Returns((CareRecipient?)null);
        _repository.GetByIdAsync(Arg.Is<int>(id => id != 3), Arg.Any<CancellationToken>())
            .Returns(new CareRecipient { Id = 1, Name = "Kari Nordmann" });

        var result = await _sut.GetByIdAsync(3, CancellationToken.None);

        Assert.Null(result);
        await _repository.Received(1).GetByIdAsync(3, Arg.Any<CancellationToken>());
    }
}
