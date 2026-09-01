using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Repositories;

[Collection(PostgresCollection.Name)]
public class EfKinshipRegistryTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase _factory = null!;

    public async Task InitializeAsync() =>
        _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private async Task<NextOfKin> SeedPersonAsync(
        string? externalId,
        string nationalIdHash,
        string displayName,
        params (string CareRecipientName, DateTimeOffset? ValidTo)[] grants
    ) =>
        await SeedPersonWithWindowsAsync(
            externalId,
            nationalIdHash,
            displayName,
            grants
                .Select(g => (g.CareRecipientName, DateTimeOffset.UtcNow.AddDays(-1), g.ValidTo))
                .ToArray()
        );

    private async Task<NextOfKin> SeedPersonWithWindowsAsync(
        string? externalId,
        string nationalIdHash,
        string displayName,
        params (
            string CareRecipientName,
            DateTimeOffset ValidFrom,
            DateTimeOffset? ValidTo
        )[] grants
    )
    {
        using var seedContext = _factory.CreateContext();

        var person = new NextOfKin
        {
            ExternalId = externalId,
            NationalIdHash = nationalIdHash,
            DisplayName = displayName,
        };

        person.Grants.AddRange(
            grants.Select(grant => new KinshipGrant
            {
                CareRecipient = new CareRecipient { Name = grant.CareRecipientName },
                ValidFrom = grant.ValidFrom,
                ValidTo = grant.ValidTo,
            })
        );

        seedContext.NextOfKin.Add(person);
        await seedContext.SaveChangesAsync();

        return person;
    }

    [Fact]
    public async Task GetByExternalIdAsync_ReturnsPersonWithGrant_WhenExternalIdMatches()
    {
        await SeedPersonAsync("sub-123", "hash-1", "Frida Sand", ("Vigdis Quist", null));

        using var context = _factory.CreateContext();
        var sut = new EfKinshipRegistry(context);

        var result = await sut.GetByExternalIdAsync("sub-123", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Frida Sand", result.DisplayName);
        var grant = Assert.Single(result.Grants);
        Assert.Equal("Vigdis Quist", grant.CareRecipient.Name);
    }

    // The Fabian case: one person, two care recipients.
    [Fact]
    public async Task GetByExternalIdAsync_ReturnsEveryCurrentGrant_WhenPersonHoldsSeveral()
    {
        await SeedPersonAsync(
            "sub-siblings",
            "hash-siblings",
            "Fabian Quist",
            ("Vigdis Quist", null),
            ("Tor Quist", null)
        );

        using var context = _factory.CreateContext();
        var sut = new EfKinshipRegistry(context);

        var result = await sut.GetByExternalIdAsync("sub-siblings", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result.Grants.Count);
        Assert.Equal(
            ["Tor Quist", "Vigdis Quist"],
            result.Grants.Select(g => g.CareRecipient.Name).OrderBy(name => name)
        );
    }

    [Fact]
    public async Task GetByExternalIdAsync_ReturnsNull_WhenNoMatch()
    {
        using var context = _factory.CreateContext();
        var sut = new EfKinshipRegistry(context);

        var result = await sut.GetByExternalIdAsync("no-such-sub", CancellationToken.None);

        Assert.Null(result);
    }

    // An expired grant drops out while the person row remains - login then
    // rejects on the empty grant set, not on a missing person
    [Fact]
    public async Task GetByExternalIdAsync_ExcludesExpiredGrant_ButStillReturnsThePerson()
    {
        await SeedPersonAsync(
            "sub-expired",
            "hash-expired",
            "Former Pårørende",
            ("Vigdis Quist", DateTimeOffset.UtcNow.AddDays(-1))
        );

        using var context = _factory.CreateContext();
        var sut = new EfKinshipRegistry(context);

        var result = await sut.GetByExternalIdAsync("sub-expired", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.Grants);
    }

    [Fact]
    public async Task GetByExternalIdAsync_KeepsOnlyTheCurrentGrant_WhenOneOfTwoHasExpired()
    {
        await SeedPersonAsync(
            "sub-mixed",
            "hash-mixed",
            "Fabian Quist",
            ("Vigdis Quist", null),
            ("Tor Quist", DateTimeOffset.UtcNow.AddDays(-1))
        );

        using var context = _factory.CreateContext();
        var sut = new EfKinshipRegistry(context);

        var result = await sut.GetByExternalIdAsync("sub-mixed", CancellationToken.None);

        Assert.NotNull(result);
        var grant = Assert.Single(result.Grants);
        Assert.Equal("Vigdis Quist", grant.CareRecipient.Name);
    }

    [Fact]
    public async Task GetByExternalIdAsync_ReturnsGrant_WhenValidToIsInFuture()
    {
        await SeedPersonAsync(
            "sub-time-limited",
            "hash-time-limited",
            "Still Active Pårørende",
            ("Vigdis Quist", DateTimeOffset.UtcNow.AddDays(1))
        );

        using var context = _factory.CreateContext();
        var sut = new EfKinshipRegistry(context);

        var result = await sut.GetByExternalIdAsync("sub-time-limited", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Still Active Pårørende", result.DisplayName);
        Assert.Single(result.Grants);
    }

    // Without these, dropping the ValidFrom half of the validity filter goes
    // undetected - a future-dated grant would authorise immediately
    [Fact]
    public async Task GetByExternalIdAsync_ExcludesGrant_WhenValidFromIsStillInTheFuture()
    {
        await SeedPersonWithWindowsAsync(
            "sub-not-yet",
            "hash-not-yet",
            "Future Pårørende",
            ("Vigdis Quist", DateTimeOffset.UtcNow.AddDays(1), null)
        );

        using var context = _factory.CreateContext();
        var sut = new EfKinshipRegistry(context);

        var result = await sut.GetByExternalIdAsync("sub-not-yet", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.Grants);
    }

    [Fact]
    public async Task GetByExternalIdAsync_KeepsOnlyTheStartedGrant_WhenOneOfTwoHasNotBegun()
    {
        await SeedPersonWithWindowsAsync(
            "sub-staggered",
            "hash-staggered",
            "Fabian Quist",
            ("Vigdis Quist", DateTimeOffset.UtcNow.AddDays(-1), null),
            ("Tor Quist", DateTimeOffset.UtcNow.AddDays(1), null)
        );

        using var context = _factory.CreateContext();
        var sut = new EfKinshipRegistry(context);

        var result = await sut.GetByExternalIdAsync("sub-staggered", CancellationToken.None);

        Assert.NotNull(result);
        var grant = Assert.Single(result.Grants);
        Assert.Equal("Vigdis Quist", grant.CareRecipient.Name);
    }

    [Fact]
    public async Task GetByNationalIdHashAsync_ExcludesGrant_WhenValidFromIsStillInTheFuture()
    {
        await SeedPersonWithWindowsAsync(
            null,
            "hash-not-yet-seed",
            "Future Pårørende",
            ("Vigdis Quist", DateTimeOffset.UtcNow.AddDays(1), null)
        );

        using var context = _factory.CreateContext();
        var sut = new EfKinshipRegistry(context);

        var result = await sut.GetByNationalIdHashAsync(
            "hash-not-yet-seed",
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Empty(result.Grants);
    }

    [Fact]
    public async Task GetByNationalIdHashAsync_ReturnsUnboundPerson_WhenHashMatches()
    {
        await SeedPersonAsync(null, "hash-seeded", "Test Testen", ("Vigdis Quist", null));

        using var context = _factory.CreateContext();
        var sut = new EfKinshipRegistry(context);

        var result = await sut.GetByNationalIdHashAsync("hash-seeded", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.ExternalId);
        Assert.Single(result.Grants);
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
    public async Task GetByNationalIdHashAsync_ExcludesExpiredGrant()
    {
        await SeedPersonAsync(
            null,
            "hash-expired-seed",
            "Former Pårørende",
            ("Vigdis Quist", DateTimeOffset.UtcNow.AddMinutes(-1))
        );

        using var context = _factory.CreateContext();
        var sut = new EfKinshipRegistry(context);

        var result = await sut.GetByNationalIdHashAsync(
            "hash-expired-seed",
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Empty(result.Grants);
    }

    [Fact]
    public async Task GetByNationalIdHashAsync_ReturnsGrant_WhenValidToIsInFuture()
    {
        await SeedPersonAsync(
            null,
            "hash-time-limited-seed",
            "Still Active Pårørende",
            ("Vigdis Quist", DateTimeOffset.UtcNow.AddDays(1))
        );

        using var context = _factory.CreateContext();
        var sut = new EfKinshipRegistry(context);

        var result = await sut.GetByNationalIdHashAsync(
            "hash-time-limited-seed",
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Single(result.Grants);
    }

    [Fact]
    public async Task UpdateAsync_BindsExternalIdAndPersists()
    {
        var seeded = await SeedPersonAsync(
            null,
            "hash-to-bind",
            "Test Testen",
            ("Vigdis Quist", null)
        );

        using (var context = _factory.CreateContext())
        {
            var sut = new EfKinshipRegistry(context);
            var person = await sut.GetByNationalIdHashAsync("hash-to-bind", CancellationToken.None);
            person!.ExternalId = "sub-newly-bound";

            await sut.UpdateAsync(person, CancellationToken.None);
        }

        using var verifyContext = _factory.CreateContext();
        var persisted = await verifyContext.NextOfKin.SingleOrDefaultAsync(n => n.Id == seeded.Id);
        Assert.NotNull(persisted);
        Assert.Equal("sub-newly-bound", persisted.ExternalId);
    }

    // Attaching the loaded graph must not duplicate the grants that came with it
    [Fact]
    public async Task UpdateAsync_LeavesGrantsUntouched()
    {
        var seeded = await SeedPersonAsync(
            null,
            "hash-grants-intact",
            "Test Testen",
            ("Vigdis Quist", null),
            ("Tor Quist", null)
        );

        using (var context = _factory.CreateContext())
        {
            var sut = new EfKinshipRegistry(context);
            var person = await sut.GetByNationalIdHashAsync(
                "hash-grants-intact",
                CancellationToken.None
            );
            person!.ExternalId = "sub-bound";

            await sut.UpdateAsync(person, CancellationToken.None);
        }

        using var verifyContext = _factory.CreateContext();
        var grantCount = await verifyContext.KinshipGrants.CountAsync(g =>
            g.NextOfKinId == seeded.Id
        );
        Assert.Equal(2, grantCount);
    }

    [Fact]
    public async Task AddingTwoPeople_WithSameNationalIdHash_ThrowsOnSecondSave()
    {
        await SeedPersonAsync(null, "duplicate-hash", "First Person", ("Vigdis Quist", null));

        using var context = _factory.CreateContext();
        context.NextOfKin.Add(
            new NextOfKin { NationalIdHash = "duplicate-hash", DisplayName = "Second Person" }
        );

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task AddingTwoPeople_WithSameExternalId_ThrowsOnSecondSave()
    {
        await SeedPersonAsync(
            "duplicate-external-id",
            "hash-for-first-person",
            "First Person",
            ("Vigdis Quist", null)
        );

        using var context = _factory.CreateContext();
        context.NextOfKin.Add(
            new NextOfKin
            {
                ExternalId = "duplicate-external-id",
                NationalIdHash = "hash-for-second-person",
                DisplayName = "Second Person",
            }
        );

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    // The pair is unique, not the person - several grants each, one per recipient
    [Fact]
    public async Task AddingTwoGrants_ForTheSamePair_ThrowsOnSecondSave()
    {
        var seeded = await SeedPersonAsync(
            null,
            "hash-pair",
            "Fabian Quist",
            ("Vigdis Quist", null)
        );

        using var context = _factory.CreateContext();
        var existingGrant = await context.KinshipGrants.FirstAsync(g => g.NextOfKinId == seeded.Id);

        context.KinshipGrants.Add(
            new KinshipGrant
            {
                NextOfKinId = seeded.Id,
                CareRecipientId = existingGrant.CareRecipientId,
            }
        );

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
