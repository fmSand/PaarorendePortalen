using Microsoft.EntityFrameworkCore;
using Npgsql;
using Parorendeportalen.Api.Models.Kinship;
using Parorendeportalen.Api.Models.Visits;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Data;

// Author and visibility both null or both set, and visibility a value the enum names,
// or the read filter reads the row wrong.
[Collection(PostgresCollection.Name)]
public class VisitAuthorshipConstraintTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private const string ConstraintName = "CK_Visits_AuthoredEntryHasVisibility";

    private PostgresTestDatabase _factory = null!;

    public async Task InitializeAsync() =>
        _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task AVisibilityWithoutAnAuthor_ViolatesTheCheckConstraint()
    {
        var vigdis = new CareRecipient { Name = "Vigdis Quist" };

        using var context = _factory.CreateContext();
        context.CareRecipients.Add(vigdis);
        context.Visits.Add(NewVisit(vigdis, author: null, Visibility.Shared));

        var postgresException = await SaveAndCaptureAsync(context);

        Assert.Equal(PostgresErrorCodes.CheckViolation, postgresException.SqlState);
        Assert.Equal(ConstraintName, postgresException.ConstraintName);
    }

    [Fact]
    public async Task AnAuthorWithoutAVisibility_ViolatesTheCheckConstraint()
    {
        var vigdis = new CareRecipient { Name = "Vigdis Quist" };
        var fabian = NewNextOfKin();

        using var context = _factory.CreateContext();
        context.CareRecipients.Add(vigdis);
        context.NextOfKin.Add(fabian);
        await context.SaveChangesAsync();

        context.Visits.Add(NewVisit(vigdis, fabian.Id, visibility: null));

        var postgresException = await SaveAndCaptureAsync(context);

        Assert.Equal(PostgresErrorCodes.CheckViolation, postgresException.SqlState);
        Assert.Equal(ConstraintName, postgresException.ConstraintName);
    }

    [Fact]
    public async Task BothSetAndBothUnset_AreBothAllowed()
    {
        var vigdis = new CareRecipient { Name = "Vigdis Quist" };
        var fabian = NewNextOfKin();

        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(vigdis);
            seedContext.NextOfKin.Add(fabian);
            await seedContext.SaveChangesAsync();

            seedContext.Visits.AddRange(
                NewVisit(vigdis, author: null, visibility: null),
                NewVisit(vigdis, fabian.Id, Visibility.Private)
            );
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();

        Assert.Equal(2, await context.Visits.CountAsync());
    }

    // Seed, ingestion and psql never pass the JSON converter, so the schema is what stops them.
    [Fact]
    public async Task AnUndefinedVisibility_ViolatesTheCheckConstraint()
    {
        var vigdis = new CareRecipient { Name = "Vigdis Quist" };
        var fabian = NewNextOfKin();

        using var context = _factory.CreateContext();
        context.CareRecipients.Add(vigdis);
        context.NextOfKin.Add(fabian);
        await context.SaveChangesAsync();

        context.Visits.Add(NewVisit(vigdis, fabian.Id, (Visibility)7));

        var postgresException = await SaveAndCaptureAsync(context);

        Assert.Equal(PostgresErrorCodes.CheckViolation, postgresException.SqlState);
        Assert.Equal("CK_Visits_Visibility", postgresException.ConstraintName);
    }

    [Fact]
    public async Task ACommentWithAnUndefinedVisibility_ViolatesTheCheckConstraint()
    {
        var vigdis = new CareRecipient { Name = "Vigdis Quist" };
        var fabian = NewNextOfKin();
        var visit = NewVisit(vigdis, author: null, visibility: null);

        using var context = _factory.CreateContext();
        context.CareRecipients.Add(vigdis);
        context.NextOfKin.Add(fabian);
        context.Visits.Add(visit);
        await context.SaveChangesAsync();

        context.VisitComments.Add(
            new VisitComment
            {
                VisitId = visit.Id,
                AuthorNextOfKinId = fabian.Id,
                Body = "Hei",
                Visibility = (Visibility)7,
            }
        );

        var postgresException = await SaveAndCaptureAsync(context);

        Assert.Equal(PostgresErrorCodes.CheckViolation, postgresException.SqlState);
        Assert.Equal("CK_VisitComments_Visibility", postgresException.ConstraintName);
    }

    private static async Task<PostgresException> SaveAndCaptureAsync(
        Parorendeportalen.Api.Data.AppDbContext context
    )
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync()
        );

        return Assert.IsType<PostgresException>(exception.InnerException);
    }

    private static NextOfKin NewNextOfKin() =>
        new() { NationalIdHash = new string('a', 64), DisplayName = "Fabian Quist" };

    private static Visit NewVisit(
        CareRecipient careRecipient,
        int? author,
        Visibility? visibility
    ) =>
        new()
        {
            CareRecipient = careRecipient,
            ScheduledAt = DateTimeOffset.UtcNow,
            Status = VisitStatus.Planned,
            Origin = Origin.Portal,
            CreatedByNextOfKinId = author,
            Visibility = visibility,
        };
}
