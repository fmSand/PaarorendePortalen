using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Tests.Services;

public class DayPlanBuilderTests
{
    private const int Vigdis = 1;
    private const int Tor = 2;

    // A settled week in September 2026, so a reader can check the weekdays.
    private static readonly DateOnly Monday = new(2026, 9, 7);
    private static readonly DateOnly Wednesday = new(2026, 9, 9);
    private static readonly DateOnly Saturday = new(2026, 9, 12);

    [Fact]
    public void Build_RuleGrantingTwiceADay_ExpandsIntoTwoOccurrences()
    {
        var plan = DayPlanBuilder.Build(Vigdis, Monday, [AVedtak(1, timesPerDay: 2)], []);

        Assert.Equal(2, plan.Items.Count);
        Assert.Equal([1, 2], plan.Items.Select(item => item.Occurrence));
        Assert.All(plan.Items, item => Assert.Equal(DayPlanItemStatus.Expected, item.Status));
        Assert.All(plan.Items, item => Assert.Equal(1, item.VedtakId));
    }

    [Fact]
    public void Build_DayTheRuleDoesNotName_GrantsNothing()
    {
        var vedtak = AVedtak(1, days: Weekdays.WeekdaysOnly, timesPerDay: 2);

        Assert.Empty(DayPlanBuilder.Build(Vigdis, Saturday, [vedtak], []).Items);
        Assert.Equal(2, DayPlanBuilder.Build(Vigdis, Monday, [vedtak], []).Items.Count);
    }

    [Theory]
    [InlineData(VedtakStatus.Draft)]
    [InlineData(VedtakStatus.OnHold)]
    [InlineData(VedtakStatus.Revoked)]
    [InlineData(VedtakStatus.Completed)]
    public void Build_VedtakNotActive_GrantsNothing(VedtakStatus status)
    {
        var vedtak = AVedtak(1, status: status);

        // OnHold is the one worth naming: dates around today, still nothing due.
        Assert.Empty(DayPlanBuilder.Build(Vigdis, Monday, [vedtak], []).Items);
    }

    [Fact]
    public void Build_VedtakStartingMidWeek_GrantsNothingOnTheDaysBeforeIt()
    {
        var vedtak = AVedtak(1, days: Weekdays.WeekdaysOnly, validFrom: Wednesday);

        Assert.Empty(DayPlanBuilder.Build(Vigdis, Monday, [vedtak], []).Items);
        Assert.Single(DayPlanBuilder.Build(Vigdis, Wednesday, [vedtak], []).Items);
    }

    [Fact]
    public void Build_VedtakEndingMidWeek_GrantsNothingOnTheDaysAfterIt()
    {
        var vedtak = AVedtak(1, days: Weekdays.WeekdaysOnly, validTo: Wednesday);

        Assert.Single(DayPlanBuilder.Build(Vigdis, Wednesday, [vedtak], []).Items);
        Assert.Empty(DayPlanBuilder.Build(Vigdis, Wednesday.AddDays(1), [vedtak], []).Items);
    }

    [Fact]
    public void Build_FirstAndLastDayOfAVedtak_AreBothInForce()
    {
        // Closed at both ends: an off-by-one at either edge costs a real day of service.
        var vedtak = AVedtak(1, validFrom: Monday, validTo: Wednesday);

        Assert.Single(DayPlanBuilder.Build(Vigdis, Monday, [vedtak], []).Items);
        Assert.Single(DayPlanBuilder.Build(Vigdis, Wednesday, [vedtak], []).Items);
        Assert.Empty(DayPlanBuilder.Build(Vigdis, Monday.AddDays(-1), [vedtak], []).Items);
        Assert.Empty(DayPlanBuilder.Build(Vigdis, Wednesday.AddDays(1), [vedtak], []).Items);
    }

    [Fact]
    public void Build_OpenEndedVedtak_IsStillInForceFarIntoTheFuture()
    {
        var vedtak = AVedtak(1, validFrom: Monday, validTo: null);

        Assert.Single(DayPlanBuilder.Build(Vigdis, Monday.AddYears(5), [vedtak], []).Items);
    }

    [Theory]
    [InlineData(VisitStatus.Planned, DayPlanItemStatus.Planned)]
    [InlineData(VisitStatus.Completed, DayPlanItemStatus.Completed)]
    [InlineData(VisitStatus.Missed, DayPlanItemStatus.Missed)]
    [InlineData(VisitStatus.Cancelled, DayPlanItemStatus.Cancelled)]
    public void Build_ReportedVisit_SettlesTheOccurrenceWithItsOwnStatus(
        VisitStatus reported,
        DayPlanItemStatus expected
    )
    {
        var visit = AVisit(10, At(Monday, 8), status: reported);

        var item = Assert.Single(DayPlanBuilder.Build(Vigdis, Monday, [AVedtak(1)], [visit]).Items);

        Assert.Equal(expected, item.Status);
        Assert.Equal(10, item.VisitId);
        Assert.Equal(1, item.VedtakId);
        Assert.Equal("Hjemmetjenesten Oslo", item.CaregiverName);
    }

    [Fact]
    public void Build_FewerVisitsThanOccurrences_LeavesTheRestExpected()
    {
        var plan = DayPlanBuilder.Build(
            Vigdis,
            Monday,
            [AVedtak(1, timesPerDay: 3)],
            [AVisit(10, At(Monday, 8))]
        );

        Assert.Equal(
            [DayPlanItemStatus.Completed, DayPlanItemStatus.Expected, DayPlanItemStatus.Expected],
            plan.Items.Select(item => item.Status)
        );
        Assert.Equal(1, plan.CompletedCount);
        Assert.Equal(2, plan.OutstandingCount);
    }

    [Fact]
    public void Build_MoreVisitsThanTheVedtakGrants_ShowsTheExtraWithNoVedtakBehindIt()
    {
        var plan = DayPlanBuilder.Build(
            Vigdis,
            Monday,
            [AVedtak(1, timesPerDay: 1)],
            [AVisit(10, At(Monday, 8)), AVisit(11, At(Monday, 16))]
        );

        // Came twice under a vedtak granting one visit; dropping the second would hide a real event.
        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(1, plan.Items[0].VedtakId);
        Assert.Null(plan.Items[1].VedtakId);
        Assert.Null(plan.Items[1].Title);
        Assert.Equal(11, plan.Items[1].VisitId);
    }

    [Fact]
    public void Build_VisitWithoutAServiceType_DoesNotSettleAnOccurrence()
    {
        // A family calendar entry is not the municipality arriving, so the occurrence stays outstanding.
        var ownAppointment = AVisit(10, At(Monday, 8), serviceType: null);

        var item = Assert.Single(
            DayPlanBuilder.Build(Vigdis, Monday, [AVedtak(1)], [ownAppointment]).Items
        );

        Assert.Equal(DayPlanItemStatus.Expected, item.Status);
        Assert.Null(item.VisitId);
    }

    [Fact]
    public void Build_VisitOfAnotherService_DoesNotSettleTheOccurrence()
    {
        var fysioterapi = AVisit(10, At(Monday, 8), serviceType: ServiceType.Fysioterapi);

        var plan = DayPlanBuilder.Build(
            Vigdis,
            Monday,
            [AVedtak(1, serviceType: ServiceType.Hjemmesykepleie)],
            [fysioterapi]
        );

        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(
            DayPlanItemStatus.Expected,
            plan.Items.Single(item => item.ServiceType == ServiceType.Hjemmesykepleie).Status
        );
        Assert.Equal(
            DayPlanItemStatus.Completed,
            plan.Items.Single(item => item.ServiceType == ServiceType.Fysioterapi).Status
        );
    }

    [Fact]
    public void Build_TwoVedtakForTheSameService_NumberTheOccurrencesOnceAcrossTheDay()
    {
        // Numbering per vedtak would show two "occurrence 1" rows, reading as a duplicate.
        var plan = DayPlanBuilder.Build(
            Vigdis,
            Monday,
            [AVedtak(1, timesPerDay: 2), AVedtak(2, timesPerDay: 1)],
            []
        );

        Assert.Equal([1, 2, 3], plan.Items.Select(item => item.Occurrence));
        Assert.Equal([1, 1, 2], plan.Items.Select(item => item.VedtakId));
    }

    [Fact]
    public void Build_VisitJustPastMidnight_BelongsToTheNorwegianDayNotTheUtcOne()
    {
        // 00:30 on the Tuesday, which UTC still calls Monday.
        var tuesday = Monday.AddDays(1);
        var justPastMidnight = AVisit(10, At(tuesday, 0, minute: 30));

        Assert.Equal(
            DayPlanItemStatus.Expected,
            Assert
                .Single(
                    DayPlanBuilder.Build(Vigdis, Monday, [AVedtak(1)], [justPastMidnight]).Items
                )
                .Status
        );
        Assert.Equal(
            DayPlanItemStatus.Completed,
            Assert
                .Single(
                    DayPlanBuilder.Build(Vigdis, tuesday, [AVedtak(1)], [justPastMidnight]).Items
                )
                .Status
        );
    }

    [Fact]
    public void Build_TheDayTheClocksGoForward_SettlesAVisitInsideIt()
    {
        var springForward = new DateOnly(2026, 3, 29);
        var visit = AVisit(10, At(springForward, 8));

        var item = Assert.Single(
            DayPlanBuilder.Build(Vigdis, springForward, [AVedtak(1)], [visit]).Items
        );

        Assert.Equal(DayPlanItemStatus.Completed, item.Status);
    }

    [Fact]
    public void Build_AcrossAMonthAndYearBoundary_BehavesLikeAnyOtherTwoDays()
    {
        // 2026-12-31 is a Thursday and 2027-01-01 a Friday: a Mon-Fri rule covers both, unaware of the month.
        var vedtak = AVedtak(1, days: Weekdays.WeekdaysOnly, validFrom: new DateOnly(2026, 1, 1));

        Assert.Single(DayPlanBuilder.Build(Vigdis, new DateOnly(2026, 12, 31), [vedtak], []).Items);
        Assert.Single(DayPlanBuilder.Build(Vigdis, new DateOnly(2027, 1, 1), [vedtak], []).Items);
    }

    [Fact]
    public void Build_Ordering_PutsBookedItemsInTimeOrderAndUnbookedAfterThem()
    {
        var plan = DayPlanBuilder.Build(
            Vigdis,
            Monday,
            [
                AVedtak(1, serviceType: ServiceType.Hjemmesykepleie, timesPerDay: 2),
                AVedtak(2, serviceType: ServiceType.Fysioterapi),
            ],
            [
                AVisit(10, At(Monday, 16)),
                AVisit(11, At(Monday, 9), serviceType: ServiceType.Fysioterapi),
            ]
        );

        Assert.Equal([11, 10, null], plan.Items.Select(item => item.VisitId));
        Assert.Null(plan.Items[2].ScheduledAt);
    }

    [Fact]
    public void Build_Counts_SeparateWhatIsDoneFromWhatIsOutstanding()
    {
        var plan = DayPlanBuilder.Build(
            Vigdis,
            Monday,
            [AVedtak(1, timesPerDay: 4)],
            [
                AVisit(10, At(Monday, 8), status: VisitStatus.Completed),
                AVisit(11, At(Monday, 12), status: VisitStatus.Planned),
                AVisit(12, At(Monday, 16), status: VisitStatus.Missed),
            ]
        );

        // Missed is neither: it is not done, and it is no longer outstanding.
        Assert.Equal(1, plan.CompletedCount);
        Assert.Equal(2, plan.OutstandingCount);
    }

    [Fact]
    public void Build_NoVedtakAndNoVisits_IsAnEmptyPlanRatherThanNothing()
    {
        var plan = DayPlanBuilder.Build(Vigdis, Saturday, [], []);

        Assert.Empty(plan.Items);
        Assert.Equal(Vigdis, plan.CareRecipientId);
        Assert.Equal(Saturday, plan.Date);
    }

    [Fact]
    public void Build_VedtakForAnotherCareRecipient_Throws()
    {
        var thrown = Assert.Throws<ArgumentException>(() =>
            DayPlanBuilder.Build(Vigdis, Monday, [AVedtak(1, careRecipientId: Tor)], [])
        );

        Assert.Contains("another", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_VisitForAnotherCareRecipient_Throws()
    {
        // Filtering it away instead would let an authorisation bug ship looking like a slow day.
        Assert.Throws<ArgumentException>(() =>
            DayPlanBuilder.Build(
                Vigdis,
                Monday,
                [],
                [AVisit(10, At(Monday, 8), careRecipientId: Tor)]
            )
        );
    }

    private static Vedtak AVedtak(
        int id,
        ServiceType serviceType = ServiceType.Hjemmesykepleie,
        Weekdays days = Weekdays.EveryDay,
        int timesPerDay = 1,
        DateOnly? validFrom = null,
        DateOnly? validTo = null,
        VedtakStatus status = VedtakStatus.Active,
        int careRecipientId = Vigdis
    ) =>
        new()
        {
            Id = id,
            CareRecipientId = careRecipientId,
            ServiceType = serviceType,
            Title = $"Vedtak {id}",
            Recurrence = new RecurrenceRule { Days = days, TimesPerDay = timesPerDay },
            ValidFrom = validFrom ?? new DateOnly(2026, 1, 1),
            ValidTo = validTo,
            Status = status,
        };

    private static Visit AVisit(
        int id,
        DateTimeOffset scheduledAt,
        VisitStatus status = VisitStatus.Completed,
        ServiceType? serviceType = ServiceType.Hjemmesykepleie,
        int careRecipientId = Vigdis
    ) =>
        new()
        {
            Id = id,
            CareRecipientId = careRecipientId,
            ScheduledAt = scheduledAt,
            ActualAt = status == VisitStatus.Completed ? scheduledAt.AddMinutes(5) : null,
            Status = status,
            ServiceType = serviceType,
            CaregiverName = "Hjemmetjenesten Oslo",
            Origin = Origin.Synthetic,
        };

    // Norwegian wall-clock time, converted the way a source would report it.
    private static DateTimeOffset At(DateOnly date, int hour, int minute = 0)
    {
        var local = date.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Unspecified);

        return new DateTimeOffset(local, NorwegianTime.Zone.GetUtcOffset(local));
    }
}
