using Microsoft.EntityFrameworkCore;
using Npgsql;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Data;

// The unique index on (ChangeEventId, NextOfKinId) is what stops a tick
// repeated after a crash from delivering twice.
[Collection(PostgresCollection.Name)]
public class NotificationConstraintTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private const string NotificationIndex = "IX_Notifications_ChangeEventId_NextOfKinId";
    private const string PreferenceIndex = "IX_NotificationPreferences_NextOfKinId_Kind";

    private PostgresTestDatabase _factory = null!;
    private int _fridaId;
    private int _vigdisId;

    public async Task InitializeAsync()
    {
        _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

        using var context = _factory.CreateContext();
        var frida = new NextOfKin { NationalIdHash = "hash-frida", DisplayName = "Frida Sand" };
        var vigdis = new CareRecipient { Name = "Vigdis Quist" };
        context.AddRange(frida, vigdis);
        await context.SaveChangesAsync();

        _fridaId = frida.Id;
        _vigdisId = vigdis.Id;
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private Notification NewNotification(long changeEventId) =>
        new()
        {
            NextOfKinId = _fridaId,
            CareRecipientId = _vigdisId,
            ChangeEventId = changeEventId,
            Category = DataCategory.Visits,
            Kind = ChangeKind.Completed,
            OccurredAt = Snapshots.Noon,
        };

    private static async Task AssertUniqueViolation(Func<Task> save, string index)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(save);

        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal(index, postgresException.ConstraintName);
    }

    [Fact]
    public async Task TwoNotifications_ForOnePersonAndOneChange_ViolateTheUniqueIndex()
    {
        using var context = _factory.CreateContext();
        context.Notifications.AddRange(NewNotification(1), NewNotification(1));

        await AssertUniqueViolation(() => context.SaveChangesAsync(), NotificationIndex);
    }

    [Fact]
    public async Task OneNotificationPerChange_IsAllowed()
    {
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.Notifications.AddRange(NewNotification(1), NewNotification(2));
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        Assert.Equal(2, await context.Notifications.CountAsync());
    }

    [Fact]
    public async Task TwoPreferences_ForOnePersonAndOneKind_ViolateTheUniqueIndex()
    {
        using var context = _factory.CreateContext();
        context.NotificationPreferences.AddRange(
            new NotificationPreference { NextOfKinId = _fridaId, Kind = ChangeKind.Added },
            new NotificationPreference { NextOfKinId = _fridaId, Kind = ChangeKind.Added }
        );

        await AssertUniqueViolation(() => context.SaveChangesAsync(), PreferenceIndex);
    }

    // The inbox and the preferences are the person's own data and go with them.
    [Fact]
    public async Task DeletingANextOfKin_TakesTheirInboxAndPreferencesWithThem()
    {
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.Notifications.Add(NewNotification(1));
            seedContext.NotificationPreferences.Add(
                new NotificationPreference { NextOfKinId = _fridaId, Kind = ChangeKind.Added }
            );
            await seedContext.SaveChangesAsync();
        }

        using (var deleteContext = _factory.CreateContext())
        {
            await deleteContext.NextOfKin.Where(n => n.Id == _fridaId).ExecuteDeleteAsync();
        }

        using var context = _factory.CreateContext();
        Assert.Empty(context.Notifications);
        Assert.Empty(context.NotificationPreferences);
    }
}
