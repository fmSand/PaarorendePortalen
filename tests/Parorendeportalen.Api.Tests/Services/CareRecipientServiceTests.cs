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
    public async Task GetAllAsync_ReturnsMappedCareRecipients()
    {
        var careRecipients = new List<CareRecipient>
        {
            new() { Id = 1, Name = "Kari Nordmann" },
            new() { Id = 2, Name = "Ola Nordmann" }
        };
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(careRecipients);

        var result = await _sut.GetAllAsync(CancellationToken.None);

        Assert.Collection(
            result,
            first =>
            {
                Assert.Equal(1, first.Id);
                Assert.Equal("Kari Nordmann", first.Name);
            },
            second =>
            {
                Assert.Equal(2, second.Id);
                Assert.Equal("Ola Nordmann", second.Name);
            });
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEmptyList_WhenRepositoryHasNone()
    {
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<CareRecipient>());

        var result = await _sut.GetAllAsync(CancellationToken.None);

        Assert.Empty(result);
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
