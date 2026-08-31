using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Repositories;

[Collection(PostgresCollection.Name)]
public class EfCareRecipientRepositoryTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase _factory = null!;

    public async Task InitializeAsync() => _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task GetByIdsAsync_ReturnsTheRequestedCareRecipients_OrderedByName()
    {
        CareRecipient ola, anne;
        using (var seedContext = _factory.CreateContext())
        {
            ola = new CareRecipient { Name = "Ola Nordmann" };
            anne = new CareRecipient { Name = "Anne Hansen" };
            seedContext.CareRecipients.AddRange(ola, anne);
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfCareRecipientRepository(context);

        var result = await sut.GetByIdsAsync([ola.Id, anne.Id], CancellationToken.None);

        Assert.Equal(["Anne Hansen", "Ola Nordmann"], result.Select(c => c.Name));
    }

    [Fact]
    public async Task GetByIdsAsync_ReturnsOnlyTheRequestedIds()
    {
        CareRecipient wanted;
        using (var seedContext = _factory.CreateContext())
        {
            wanted = new CareRecipient { Name = "Anne Hansen" };
            seedContext.CareRecipients.AddRange(wanted, new CareRecipient { Name = "Ola Nordmann" });
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfCareRecipientRepository(context);

        var result = await sut.GetByIdsAsync([wanted.Id], CancellationToken.None);

        Assert.Equal(["Anne Hansen"], result.Select(c => c.Name));
    }

    // A caller with no grants must not fall through to an unfiltered query
    [Fact]
    public async Task GetByIdsAsync_ReturnsEmpty_WhenNoIdsRequested()
    {
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(new CareRecipient { Name = "Ola Nordmann" });
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfCareRecipientRepository(context);

        var result = await sut.GetByIdsAsync([], CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsCareRecipient_WhenIdExists()
    {
        CareRecipient kari;
        using (var seedContext = _factory.CreateContext())
        {
            kari = new CareRecipient { Name = "Kari Nordmann" };
            seedContext.CareRecipients.Add(kari);
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfCareRecipientRepository(context);

        var result = await sut.GetByIdAsync(kari.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(kari.Id, result.Id);
        Assert.Equal("Kari Nordmann", result.Name);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenIdDoesNotExist()
    {
        using var context = _factory.CreateContext();
        var sut = new EfCareRecipientRepository(context);

        var result = await sut.GetByIdAsync(id: 12345, CancellationToken.None);

        Assert.Null(result);
    }
}
