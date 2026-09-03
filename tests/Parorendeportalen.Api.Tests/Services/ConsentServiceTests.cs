using NSubstitute;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Services;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Services;

public class ConsentServiceTests
{
    private readonly IConsentRepository _repository = Substitute.For<IConsentRepository>();
    private readonly FixedTimeProvider _clock = new(Snapshots.Noon);
    private readonly ConsentService _sut;

    public ConsentServiceTests()
    {
        _sut = new ConsentService(_repository, _clock);
    }

    [Fact]
    public async Task PassesThePairThrough_AndEvaluatesTheWindowAtTheClocksNow()
    {
        _repository
            .GetActiveCategoriesAsync(5, 7, Snapshots.Noon, Arg.Any<CancellationToken>())
            .Returns([DataCategory.Visits]);

        var categories = await _sut.GetConsentedCategoriesAsync(5, 7, CancellationToken.None);

        Assert.Equal([DataCategory.Visits], categories);
    }

    // A stale clock would keep reporting a consent that has since been revoked.
    [Fact]
    public async Task UsesTheCurrentTime_WhenTheClockHasMovedOn()
    {
        _clock.Now = Snapshots.Noon.AddDays(2);

        await _sut.GetConsentedCategoriesAsync(5, 7, CancellationToken.None);

        await _repository
            .Received(1)
            .GetActiveCategoriesAsync(
                5,
                7,
                Snapshots.Noon.AddDays(2),
                Arg.Any<CancellationToken>()
            );
    }
}
