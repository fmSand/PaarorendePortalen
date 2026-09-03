using Microsoft.EntityFrameworkCore;
using Npgsql;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Data;

// The index that keeps a national identifier resolving to one person.
[Collection(PostgresCollection.Name)]
public class CareRecipientIdentityConstraintTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private const string IndexName = "IX_CareRecipients_NationalIdHash";

    private PostgresTestDatabase _factory = null!;

    public async Task InitializeAsync() =>
        _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task TwoCareRecipientsWithTheSameHash_ViolateTheUniqueIndex()
    {
        using var context = _factory.CreateContext();
        context.CareRecipients.AddRange(
            new CareRecipient { Name = "Vigdis Quist", NationalIdHash = Hash('a') },
            new CareRecipient { Name = "Vigdis Q.", NationalIdHash = Hash('a') }
        );

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync()
        );

        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal(IndexName, postgresException.ConstraintName);
    }

    // The filter's case: the portal holds people it has no number for.
    [Fact]
    public async Task ManyCareRecipientsWithoutAHash_AreAllowed()
    {
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.AddRange(
                new CareRecipient { Name = "Vigdis Quist" },
                new CareRecipient { Name = "Tor Quist" },
                new CareRecipient { Name = "Kari Nordmann" }
            );
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();

        Assert.Equal(3, await context.CareRecipients.CountAsync(c => c.NationalIdHash == null));
    }

    private static string Hash(char fill) => new(fill, 64);
}
