using Microsoft.EntityFrameworkCore;
using Npgsql;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Data;

// Two open consents for one triple would make "revoke the consent" ambiguous.
// Closed rows are history and may pile up.
[Collection(PostgresCollection.Name)]
public class ConsentConstraintTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private const string IndexName = "IX_Consents_CareRecipientId_NextOfKinId_Category";

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

    private Consent NewConsent(DataCategory category, DateTimeOffset? validTo = null) =>
        new()
        {
            NextOfKinId = _fridaId,
            CareRecipientId = _vigdisId,
            Category = category,
            ValidFrom = Snapshots.Noon.AddDays(-1),
            ValidTo = validTo,
        };

    [Fact]
    public async Task TwoOpenConsents_ForOneTriple_ViolateTheUniqueIndex()
    {
        using var context = _factory.CreateContext();
        context.Consents.AddRange(NewConsent(DataCategory.Visits), NewConsent(DataCategory.Visits));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync()
        );

        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal(IndexName, postgresException.ConstraintName);
    }

    [Fact]
    public async Task AClosedConsent_BesideAnOpenOne_IsAllowed()
    {
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.Consents.AddRange(
                NewConsent(DataCategory.Visits, validTo: Snapshots.Noon.AddHours(-1)),
                NewConsent(DataCategory.Visits)
            );
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();

        Assert.Equal(2, await context.Consents.CountAsync());
    }

    // Otherwise an index on the pair alone would satisfy the test above.
    [Fact]
    public async Task OneOpenConsentPerCategory_IsAllowed()
    {
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.Consents.AddRange(
                NewConsent(DataCategory.Visits),
                NewConsent(DataCategory.Medications)
            );
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();

        Assert.Equal(2, await context.Consents.CountAsync());
    }
}
