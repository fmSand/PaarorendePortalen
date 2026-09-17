using System.Globalization;
using NSubstitute;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Models.Planning;
using Parorendeportalen.Api.Models.Visits;
using Parorendeportalen.Api.Repositories.Planning;
using Parorendeportalen.Api.Repositories.Visits;
using Parorendeportalen.Api.Services;
using Parorendeportalen.Api.Services.Kinship;
using Parorendeportalen.Api.Services.Planning;

namespace Parorendeportalen.Api.Tests.Services.Planning;

public class DayPlanServiceTests
{
    private const int Vigdis = 1;
    private const int Fabian = 4;

    private static readonly DateOnly Monday = new(2026, 9, 7);

    private readonly IVedtakRepository _vedtak = Substitute.For<IVedtakRepository>();
    private readonly IVisitRepository _visits = Substitute.For<IVisitRepository>();
    private readonly ICurrentNextOfKinAccessor _currentNextOfKin =
        Substitute.For<ICurrentNextOfKinAccessor>();
    private readonly DayPlanService _sut;

    public DayPlanServiceTests()
    {
        _vedtak
            .GetInForceOnAsync(Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _visits
            .GetInRangeAsync(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>()
            )
            .Returns([]);
        _currentNextOfKin
            .GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(new CurrentNextOfKin(Fabian, [Vigdis]));

        _sut = new DayPlanService(_vedtak, _visits, _currentNextOfKin);
    }

    // Bounds written out as UTC instants, since NorwegianTime.BoundsOf is the call the service
    // itself makes. The 23- and 25-hour days are the cases that matter.
    [Theory]
    [InlineData("2026-09-07", "2026-09-06T22:00:00Z", "2026-09-07T22:00:00Z", 24)]
    [InlineData("2026-03-29", "2026-03-28T23:00:00Z", "2026-03-29T22:00:00Z", 23)]
    [InlineData("2026-10-25", "2026-10-24T22:00:00Z", "2026-10-25T23:00:00Z", 25)]
    public async Task GetAsync_AsksForVisitsInsideTheNorwegianDay(
        string date,
        string expectedFrom,
        string expectedTo,
        int expectedHours
    )
    {
        var requested = DateOnly.Parse(date, CultureInfo.InvariantCulture);
        var from = DateTimeOffset.Parse(expectedFrom, CultureInfo.InvariantCulture);
        var to = DateTimeOffset.Parse(expectedTo, CultureInfo.InvariantCulture);

        await _sut.GetAsync(Vigdis, requested, CancellationToken.None);

        Assert.Equal(TimeSpan.FromHours(expectedHours), to - from);
        await _visits
            .Received(1)
            .GetInRangeAsync(Vigdis, Fabian, from, to, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_ScopesBothReadsToTheCareRecipientAndTheDate()
    {
        await _sut.GetAsync(Vigdis, Monday, CancellationToken.None);

        await _vedtak.Received(1).GetInForceOnAsync(Vigdis, Monday, Arg.Any<CancellationToken>());
        await _visits
            .Received(1)
            .GetInRangeAsync(
                Vigdis,
                Fabian,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GetAsync_BuildsThePlanFromBothReads()
    {
        var scheduledAt = At(Monday, 8);
        _vedtak
            .GetInForceOnAsync(Vigdis, Monday, Arg.Any<CancellationToken>())
            .Returns([AVedtakGranting(timesPerDay: 2)]);
        _visits
            .GetInRangeAsync(
                Vigdis,
                Fabian,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>()
            )
            .Returns([ACompletedVisit(10, scheduledAt)]);

        var plan = await _sut.GetAsync(Vigdis, Monday, CancellationToken.None);

        Assert.Equal(Vigdis, plan.CareRecipientId);
        Assert.Equal(Monday, plan.Date);
        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(1, plan.CompletedCount);
        Assert.Equal(1, plan.OutstandingCount);
        Assert.Equal(DayPlanItemStatus.Completed, plan.Items[0].Status);
        Assert.Equal(10, plan.Items[0].VisitId);
        Assert.Equal(scheduledAt, plan.Items[0].ScheduledAt);
        Assert.Equal(DayPlanItemStatus.Expected, plan.Items[1].Status);
    }

    [Fact]
    public async Task GetAsync_NothingGrantedAndNothingReported_IsAnEmptyPlanNotAFailure()
    {
        var plan = await _sut.GetAsync(Vigdis, Monday, CancellationToken.None);

        Assert.Empty(plan.Items);
        Assert.Equal(0, plan.CompletedCount);
        Assert.Equal(0, plan.OutstandingCount);
    }

    private static DateTimeOffset At(DateOnly date, int hour)
    {
        var local = date.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Unspecified);

        return new DateTimeOffset(local, NorwegianTime.Zone.GetUtcOffset(local));
    }

    private static Vedtak AVedtakGranting(int timesPerDay) =>
        new()
        {
            Id = 1,
            CareRecipientId = Vigdis,
            ServiceType = ServiceType.Hjemmesykepleie,
            Title = "Vedtak 1",
            Recurrence = new RecurrenceRule { Days = Weekdays.EveryDay, TimesPerDay = timesPerDay },
            ValidFrom = new DateOnly(2026, 1, 1),
            Status = VedtakStatus.Active,
        };

    private static Visit ACompletedVisit(int id, DateTimeOffset scheduledAt) =>
        new()
        {
            Id = id,
            CareRecipientId = Vigdis,
            ScheduledAt = scheduledAt,
            ActualAt = scheduledAt.AddMinutes(5),
            Status = VisitStatus.Completed,
            ServiceType = ServiceType.Hjemmesykepleie,
            Origin = Origin.Synthetic,
        };
}
