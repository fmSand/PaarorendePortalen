using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Repositories;

public class EfCareRecipientRepositoryTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task GetAllAsync_ReturnsAllCareRecipients_OrderedByName()
    {
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.AddRange(
                new CareRecipient { Name = "Ola Nordmann" },
                new CareRecipient { Name = "Anne Hansen" });
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfCareRecipientRepository(context);

        var result = await sut.GetAllAsync(CancellationToken.None);

        Assert.Equal(["Anne Hansen", "Ola Nordmann"], result.Select(c => c.Name));
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
