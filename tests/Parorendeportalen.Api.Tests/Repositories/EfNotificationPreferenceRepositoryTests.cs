using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Repositories;

[Collection(PostgresCollection.Name)]
public class EfNotificationPreferenceRepositoryTests(PostgresContainerFixture fixture)
    : IAsyncLifetime
{
    private PostgresTestDatabase _factory = null!;
    private int _fridaId;
    private int _fabianId;

    public async Task InitializeAsync()
    {
        _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

        using var context = _factory.CreateContext();
        var frida = new NextOfKin { NationalIdHash = "hash-frida", DisplayName = "Frida Sand" };
        var fabian = new NextOfKin { NationalIdHash = "hash-fabian", DisplayName = "Fabian Quist" };
        context.AddRange(frida, fabian);
        await context.SaveChangesAsync();

        _fridaId = frida.Id;
        _fabianId = fabian.Id;
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private async Task SetAsync(int nextOfKinId, ChangeKind kind, bool enabled)
    {
        using var context = _factory.CreateContext();
        var sut = new EfNotificationPreferenceRepository(context);
        await sut.SetAsync(nextOfKinId, kind, enabled, CancellationToken.None);
    }

    private async Task<IReadOnlyList<NotificationPreference>> GetAsync(int nextOfKinId)
    {
        using var context = _factory.CreateContext();
        var sut = new EfNotificationPreferenceRepository(context);
        return await sut.GetAsync(nextOfKinId, CancellationToken.None);
    }

    private async Task<IReadOnlySet<(int NextOfKinId, ChangeKind Kind)>> DisabledFor(
        params int[] nextOfKinIds
    )
    {
        using var context = _factory.CreateContext();
        var sut = new EfNotificationPreferenceRepository(context);
        return await sut.GetDisabledAsync(nextOfKinIds, CancellationToken.None);
    }

    [Fact]
    public async Task Get_ReturnsOnlyThePersonsOwnRows()
    {
        await SetAsync(_fridaId, ChangeKind.Completed, enabled: false);
        await SetAsync(_fabianId, ChangeKind.Missed, enabled: false);

        var rows = await GetAsync(_fridaId);

        var row = Assert.Single(rows);
        Assert.Equal(ChangeKind.Completed, row.Kind);
        Assert.False(row.Enabled);
    }

    [Fact]
    public async Task Get_ReturnsNothing_WhenNothingWasChosen()
    {
        Assert.Empty(await GetAsync(_fridaId));
    }

    [Fact]
    public async Task Set_InsertsThenUpdates_LeavingOneRowPerKind()
    {
        await SetAsync(_fridaId, ChangeKind.Completed, enabled: false);
        await SetAsync(_fridaId, ChangeKind.Completed, enabled: true);

        var row = Assert.Single(await GetAsync(_fridaId));
        Assert.True(row.Enabled);

        using var context = _factory.CreateContext();
        Assert.Equal(1, await context.NotificationPreferences.CountAsync());
    }

    [Fact]
    public async Task Disabled_ReturnsTheSwitchedOffPairs_ForTheAskedPeopleOnly()
    {
        await SetAsync(_fridaId, ChangeKind.Completed, enabled: false);
        await SetAsync(_fridaId, ChangeKind.Missed, enabled: true);
        await SetAsync(_fabianId, ChangeKind.Added, enabled: false);

        var disabled = await DisabledFor(_fridaId);

        Assert.Equal([(_fridaId, ChangeKind.Completed)], disabled);
    }

    [Fact]
    public async Task Disabled_CoversSeveralPeopleAtOnce()
    {
        await SetAsync(_fridaId, ChangeKind.Completed, enabled: false);
        await SetAsync(_fabianId, ChangeKind.Added, enabled: false);

        var disabled = await DisabledFor(_fridaId, _fabianId);

        Assert.Equal(2, disabled.Count);
        Assert.Contains((_fridaId, ChangeKind.Completed), disabled);
        Assert.Contains((_fabianId, ChangeKind.Added), disabled);
    }

    [Fact]
    public async Task Disabled_IsEmpty_ForNobody()
    {
        await SetAsync(_fridaId, ChangeKind.Completed, enabled: false);

        Assert.Empty(await DisabledFor());
    }
}
