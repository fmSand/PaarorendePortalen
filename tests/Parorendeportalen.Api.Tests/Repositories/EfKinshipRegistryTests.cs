using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Repositories;

[Collection(PostgresCollection.Name)]
public class EfKinshipRegistryTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase _factory = null!;

    public async Task InitializeAsync() => _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task GetByExternalIdAsync_ReturnsGrant_WhenExternalIdMatches()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(kari);
            seedContext.NextOfKin.Add(new NextOfKin
            {
                ExternalId = "sub-123",
                NationalIdHash = "hash-1",
                DisplayName = "Frida Sand",
                CareRecipient = kari
            });
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfKinshipRegistry(context);

        var result = await sut.GetByExternalIdAsync("sub-123", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Frida Sand", result.DisplayName);
        Assert.Equal(kari.Id, result.CareRecipientId);
    }

    [Fact]
    public async Task GetByExternalIdAsync_ReturnsNull_WhenNoMatch()
    {
        using var context = _factory.CreateContext();
        var sut = new EfKinshipRegistry(context);

        var result = await sut.GetByExternalIdAsync("no-such-sub", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByExternalIdAsync_ReturnsNull_WhenGrantHasExpired()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(kari);
            seedContext.NextOfKin.Add(new NextOfKin
            {
                ExternalId = "sub-expired",
                NationalIdHash = "hash-expired",
                DisplayName = "Former Pårørende",
                CareRecipient = kari,
                ValidTo = DateTimeOffset.UtcNow.AddDays(-1)
            });
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfKinshipRegistry(context);

        var result = await sut.GetByExternalIdAsync("sub-expired", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByNationalIdHashAsync_ReturnsUnboundGrant_WhenHashMatches()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(kari);
            seedContext.NextOfKin.Add(new NextOfKin
            {
                ExternalId = null,
                NationalIdHash = "hash-seeded",
                DisplayName = "Test Testen",
                CareRecipient = kari
            });
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfKinshipRegistry(context);

        var result = await sut.GetByNationalIdHashAsync("hash-seeded", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.ExternalId);
        Assert.Equal(kari.Id, result.CareRecipientId);
    }

    [Fact]
    public async Task GetByNationalIdHashAsync_ReturnsNull_WhenNoMatch()
    {
        using var context = _factory.CreateContext();
        var sut = new EfKinshipRegistry(context);

        var result = await sut.GetByNationalIdHashAsync("no-such-hash", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByNationalIdHashAsync_ReturnsNull_WhenGrantHasExpired()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(kari);
            seedContext.NextOfKin.Add(new NextOfKin
            {
                NationalIdHash = "hash-expired-seed",
                DisplayName = "Former Pårørende",
                CareRecipient = kari,
                ValidTo = DateTimeOffset.UtcNow.AddMinutes(-1)
            });
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfKinshipRegistry(context);

        var result = await sut.GetByNationalIdHashAsync("hash-expired-seed", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_BindsExternalIdAndPersists()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        NextOfKin seeded;
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(kari);
            seeded = new NextOfKin
            {
                ExternalId = null,
                NationalIdHash = "hash-to-bind",
                DisplayName = "Test Testen",
                CareRecipient = kari
            };
            seedContext.NextOfKin.Add(seeded);
            await seedContext.SaveChangesAsync();
        }

        using (var context = _factory.CreateContext())
        {
            var sut = new EfKinshipRegistry(context);
            var grant = await sut.GetByNationalIdHashAsync("hash-to-bind", CancellationToken.None);
            grant!.ExternalId = "sub-newly-bound";

            await sut.UpdateAsync(grant, CancellationToken.None);
        }

        using var verifyContext = _factory.CreateContext();
        var persisted = await verifyContext.NextOfKin.SingleOrDefaultAsync(n => n.Id == seeded.Id);
        Assert.NotNull(persisted);
        Assert.Equal("sub-newly-bound", persisted.ExternalId);
    }

    [Fact]
    public async Task AddingTwoGrants_WithSameNationalIdHash_ThrowsOnSecondSave()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(kari);
            seedContext.NextOfKin.Add(new NextOfKin
            {
                NationalIdHash = "duplicate-hash",
                DisplayName = "First Person",
                CareRecipient = kari
            });
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        context.NextOfKin.Add(new NextOfKin
        {
            NationalIdHash = "duplicate-hash",
            DisplayName = "Second Person",
            CareRecipientId = kari.Id
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task AddingTwoGrants_WithSameExternalId_ThrowsOnSecondSave()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(kari);
            seedContext.NextOfKin.Add(new NextOfKin
            {
                ExternalId = "duplicate-external-id",
                NationalIdHash = "hash-for-first-person",
                DisplayName = "First Person",
                CareRecipient = kari
            });
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        context.NextOfKin.Add(new NextOfKin
        {
            ExternalId = "duplicate-external-id",
            NationalIdHash = "hash-for-second-person",
            DisplayName = "Second Person",
            CareRecipientId = kari.Id
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task GetByExternalIdAsync_ReturnsGrant_WhenValidToIsInFuture()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(kari);
            seedContext.NextOfKin.Add(new NextOfKin
            {
                ExternalId = "sub-time-limited",
                NationalIdHash = "hash-time-limited",
                DisplayName = "Still Active Pårørende",
                CareRecipient = kari,
                ValidTo = DateTimeOffset.UtcNow.AddDays(1)
            });
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfKinshipRegistry(context);

        var result = await sut.GetByExternalIdAsync("sub-time-limited", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Still Active Pårørende", result.DisplayName);
    }

    [Fact]
    public async Task GetByNationalIdHashAsync_ReturnsGrant_WhenValidToIsInFuture()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(kari);
            seedContext.NextOfKin.Add(new NextOfKin
            {
                NationalIdHash = "hash-time-limited-seed",
                DisplayName = "Still Active Pårørende",
                CareRecipient = kari,
                ValidTo = DateTimeOffset.UtcNow.AddDays(1)
            });
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfKinshipRegistry(context);

        var result = await sut.GetByNationalIdHashAsync("hash-time-limited-seed", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(kari.Id, result.CareRecipientId);
    }
}
