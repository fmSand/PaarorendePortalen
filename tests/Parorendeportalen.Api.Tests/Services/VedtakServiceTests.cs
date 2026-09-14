using NSubstitute;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Tests.Services;

public class VedtakServiceTests
{
    private const int Vigdis = 1;

    private readonly IVedtakRepository _repository = Substitute.For<IVedtakRepository>();
    private readonly VedtakService _sut;

    public VedtakServiceTests()
    {
        _sut = new VedtakService(_repository);
    }

    [Fact]
    public async Task GetByCareRecipientIdAsync_MapsTheRuleToDaysAWireFormatCanRead()
    {
        _repository
            .GetByCareRecipientIdAsync(Vigdis, Arg.Any<CancellationToken>())
            .Returns([
                AVedtak(
                    1,
                    days: Weekdays.Friday | Weekdays.Monday,
                    timesPerDay: 2,
                    title: "Hjemmesykepleie x2/dag"
                ),
            ]);

        var response = Assert.Single(
            await _sut.GetByCareRecipientIdAsync(Vigdis, CancellationToken.None)
        );

        // Stored as a flag set; a client should get day names in week order, never the bit values.
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Friday], response.Recurrence.Days);
        Assert.Equal(2, response.Recurrence.TimesPerDay);
        Assert.Equal("Hjemmesykepleie x2/dag", response.Title);
        Assert.Equal(ServiceType.Hjemmesykepleie, response.ServiceType);
        Assert.Equal(Vigdis, response.CareRecipientId);
    }

    [Fact]
    public async Task GetByCareRecipientIdAsync_KeepsTheOrderTheRepositoryReturned()
    {
        _repository
            .GetByCareRecipientIdAsync(Vigdis, Arg.Any<CancellationToken>())
            .Returns([AVedtak(3, title: "third"), AVedtak(1, title: "first")]);

        var response = await _sut.GetByCareRecipientIdAsync(Vigdis, CancellationToken.None);

        // Ordering is the query's job; re-sorting here would silently override it.
        Assert.Equal(["third", "first"], response.Select(v => v.Title));
    }

    [Fact]
    public async Task GetByCareRecipientIdAsync_MapsTasksInTheOrderTheyCameBack()
    {
        var vedtak = AVedtak(1);
        vedtak.Tasks.Add(
            new VedtakTask
            {
                Id = 10,
                Description = "Morgenstell",
                Sequence = 1,
            }
        );
        vedtak.Tasks.Add(
            new VedtakTask
            {
                Id = 11,
                Description = "Medisiner",
                Sequence = 2,
            }
        );
        _repository
            .GetByCareRecipientIdAsync(Vigdis, Arg.Any<CancellationToken>())
            .Returns([vedtak]);

        var response = Assert.Single(
            await _sut.GetByCareRecipientIdAsync(Vigdis, CancellationToken.None)
        );

        Assert.Equal(["Morgenstell", "Medisiner"], response.Tasks.Select(task => task.Description));
        Assert.Equal([10, 11], response.Tasks.Select(task => task.Id));
        Assert.Equal([1, 2], response.Tasks.Select(task => task.Sequence));
    }

    [Fact]
    public async Task GetByCareRecipientIdAsync_NoVedtak_IsAnEmptyList()
    {
        _repository.GetByCareRecipientIdAsync(Vigdis, Arg.Any<CancellationToken>()).Returns([]);

        Assert.Empty(await _sut.GetByCareRecipientIdAsync(Vigdis, CancellationToken.None));
    }

    [Fact]
    public async Task GetByIdAsync_PassesTheCareRecipientScopeThroughToTheRepository()
    {
        _repository
            .GetByIdAsync(42, Vigdis, Arg.Any<CancellationToken>())
            .Returns(AVedtak(42, title: "Fysioterapi hver onsdag"));

        var response = await _sut.GetByIdAsync(42, Vigdis, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(42, response.Id);
        Assert.Equal("Fysioterapi hver onsdag", response.Title);
        // Dropping the scope here would make the repository's BOLA filter unreachable.
        await _repository.Received(1).GetByIdAsync(42, Vigdis, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByIdAsync_RepositoryFoundNothing_IsNull()
    {
        _repository
            .GetByIdAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((Vedtak?)null);

        Assert.Null(await _sut.GetByIdAsync(42, Vigdis, CancellationToken.None));
    }

    [Fact]
    public async Task GetByIdAsync_MapsTheDatesAndStatusThrough()
    {
        var validFrom = new DateOnly(2026, 1, 15);
        var validTo = new DateOnly(2026, 12, 31);
        _repository
            .GetByIdAsync(1, Vigdis, Arg.Any<CancellationToken>())
            .Returns(
                AVedtak(1, validFrom: validFrom, validTo: validTo, status: VedtakStatus.OnHold)
            );

        var response = await _sut.GetByIdAsync(1, Vigdis, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(validFrom, response.ValidFrom);
        Assert.Equal(validTo, response.ValidTo);
        Assert.Equal(VedtakStatus.OnHold, response.Status);
    }

    private static Vedtak AVedtak(
        int id,
        ServiceType serviceType = ServiceType.Hjemmesykepleie,
        Weekdays days = Weekdays.EveryDay,
        int timesPerDay = 1,
        DateOnly? validFrom = null,
        DateOnly? validTo = null,
        VedtakStatus status = VedtakStatus.Active,
        string title = "Vedtak"
    ) =>
        new()
        {
            Id = id,
            CareRecipientId = Vigdis,
            ServiceType = serviceType,
            Title = title,
            Recurrence = new RecurrenceRule { Days = days, TimesPerDay = timesPerDay },
            ValidFrom = validFrom ?? new DateOnly(2026, 1, 1),
            ValidTo = validTo,
            Status = status,
        };
}
