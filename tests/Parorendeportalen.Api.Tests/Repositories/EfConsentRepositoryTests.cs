using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Repositories;

[Collection(PostgresCollection.Name)]
public class EfConsentRepositoryTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = Snapshots.Noon;

    private PostgresTestDatabase _factory = null!;
    private int _fridaId;
    private int _fabianId;
    private int _vigdisId;
    private int _torId;

    public async Task InitializeAsync()
    {
        _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

        using var context = _factory.CreateContext();
        var frida = new NextOfKin { NationalIdHash = "hash-frida", DisplayName = "Frida Sand" };
        var fabian = new NextOfKin { NationalIdHash = "hash-fabian", DisplayName = "Fabian Quist" };
        var vigdis = new CareRecipient { Name = "Vigdis Quist" };
        var tor = new CareRecipient { Name = "Tor Quist" };
        context.AddRange(frida, fabian, vigdis, tor);
        await context.SaveChangesAsync();

        _fridaId = frida.Id;
        _fabianId = fabian.Id;
        _vigdisId = vigdis.Id;
        _torId = tor.Id;
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private async Task SeedConsentAsync(
        int nextOfKinId,
        int careRecipientId,
        DataCategory category,
        DateTimeOffset? validFrom = null,
        DateTimeOffset? validTo = null
    )
    {
        using var context = _factory.CreateContext();
        context.Consents.Add(
            new Consent
            {
                NextOfKinId = nextOfKinId,
                CareRecipientId = careRecipientId,
                Category = category,
                ValidFrom = validFrom ?? Now.AddDays(-1),
                ValidTo = validTo,
            }
        );
        await context.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<DataCategory>> ActiveFor(
        int nextOfKinId,
        int careRecipientId,
        DateTimeOffset? asOf = null
    )
    {
        using var context = _factory.CreateContext();
        var sut = new EfConsentRepository(context);
        return await sut.GetActiveCategoriesAsync(
            nextOfKinId,
            careRecipientId,
            asOf ?? Now,
            CancellationToken.None
        );
    }

    [Fact]
    public async Task ReturnsTheCategory_OfAnOpenConsent()
    {
        await SeedConsentAsync(_fridaId, _vigdisId, DataCategory.Visits);

        Assert.Equal([DataCategory.Visits], await ActiveFor(_fridaId, _vigdisId));
    }

    [Fact]
    public async Task ReturnsEmpty_WhenNoConsentExists()
    {
        Assert.Empty(await ActiveFor(_fridaId, _vigdisId));
    }

    [Fact]
    public async Task ExcludesAConsent_WhoseValidToHasPassed()
    {
        await SeedConsentAsync(
            _fridaId,
            _vigdisId,
            DataCategory.Visits,
            validTo: Now.AddMinutes(-1)
        );

        Assert.Empty(await ActiveFor(_fridaId, _vigdisId));
    }

    [Fact]
    public async Task IncludesAConsent_WhoseValidToIsStillAhead()
    {
        await SeedConsentAsync(_fridaId, _vigdisId, DataCategory.Visits, validTo: Now.AddDays(1));

        Assert.Equal([DataCategory.Visits], await ActiveFor(_fridaId, _vigdisId));
    }

    [Fact]
    public async Task ExcludesAConsent_WhoseValidFromIsStillAhead()
    {
        await SeedConsentAsync(_fridaId, _vigdisId, DataCategory.Visits, validFrom: Now.AddDays(1));

        Assert.Empty(await ActiveFor(_fridaId, _vigdisId));
    }

    // Evaluated at the instant handed in, which is what lets the policy log the
    // same instant it decided on.
    [Fact]
    public async Task EvaluatesTheWindow_AtTheInstantHandedIn()
    {
        await SeedConsentAsync(
            _fridaId,
            _vigdisId,
            DataCategory.Visits,
            validFrom: Now.AddDays(-1),
            validTo: Now.AddDays(1)
        );

        Assert.Equal([DataCategory.Visits], await ActiveFor(_fridaId, _vigdisId, asOf: Now));
        Assert.Empty(await ActiveFor(_fridaId, _vigdisId, asOf: Now.AddDays(-2)));
        Assert.Empty(await ActiveFor(_fridaId, _vigdisId, asOf: Now.AddDays(2)));
    }

    [Fact]
    public async Task ReturnsEveryOpenCategory_WhenSeveralAreShared()
    {
        await SeedConsentAsync(_fridaId, _vigdisId, DataCategory.Visits);
        await SeedConsentAsync(_fridaId, _vigdisId, DataCategory.Medications);

        var categories = await ActiveFor(_fridaId, _vigdisId);

        Assert.Equal(
            [DataCategory.Visits, DataCategory.Medications],
            categories.OrderBy(category => category)
        );
    }

    [Fact]
    public async Task IsScopedToTheNextOfKin_AndToTheCareRecipient()
    {
        await SeedConsentAsync(_fabianId, _vigdisId, DataCategory.Visits);
        await SeedConsentAsync(_fridaId, _torId, DataCategory.Visits);

        Assert.Empty(await ActiveFor(_fridaId, _vigdisId));
        Assert.Equal([DataCategory.Visits], await ActiveFor(_fabianId, _vigdisId));
        Assert.Equal([DataCategory.Visits], await ActiveFor(_fridaId, _torId));
    }

    [Fact]
    public async Task ReturnsACategoryOnce_WhenARevokedRowSitsBesideAnOpenOne()
    {
        await SeedConsentAsync(
            _fridaId,
            _vigdisId,
            DataCategory.Visits,
            validFrom: Now.AddDays(-10),
            validTo: Now.AddDays(-5)
        );
        await SeedConsentAsync(_fridaId, _vigdisId, DataCategory.Visits);

        Assert.Equal([DataCategory.Visits], await ActiveFor(_fridaId, _vigdisId));
    }

    private async Task SeedGrantAsync(
        int nextOfKinId,
        int careRecipientId,
        DateTimeOffset? validTo = null
    )
    {
        using var context = _factory.CreateContext();
        context.KinshipGrants.Add(
            new KinshipGrant
            {
                NextOfKinId = nextOfKinId,
                CareRecipientId = careRecipientId,
                ValidFrom = Now.AddDays(-1),
                ValidTo = validTo,
            }
        );
        await context.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<ConsentScope>> ScopesOf(int nextOfKinId)
    {
        using var context = _factory.CreateContext();
        var sut = new EfConsentRepository(context);
        return await sut.GetActiveScopesAsync(nextOfKinId, Now, CancellationToken.None);
    }

    private async Task<IReadOnlyList<int>> ConsentedTo(
        int careRecipientId,
        DataCategory category = DataCategory.Visits,
        DateTimeOffset? asOf = null
    )
    {
        using var context = _factory.CreateContext();
        var sut = new EfConsentRepository(context);
        return await sut.GetConsentedNextOfKinIdsAsync(
            careRecipientId,
            category,
            asOf ?? Now,
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Scopes_ReturnEveryOpenPair_AcrossCareRecipientsAndCategories()
    {
        await SeedConsentAsync(_fridaId, _vigdisId, DataCategory.Visits);
        await SeedConsentAsync(_fridaId, _vigdisId, DataCategory.Medications);
        await SeedConsentAsync(_fridaId, _torId, DataCategory.Visits);

        var scopes = await ScopesOf(_fridaId);

        Assert.Equal(
            [
                new ConsentScope(_vigdisId, DataCategory.Visits),
                new ConsentScope(_vigdisId, DataCategory.Medications),
                new ConsentScope(_torId, DataCategory.Visits),
            ],
            scopes.OrderBy(s => s.CareRecipientId).ThenBy(s => s.Category)
        );
    }

    [Fact]
    public async Task Scopes_ExcludeAClosedConsent_AndAnotherPersons()
    {
        await SeedConsentAsync(
            _fridaId,
            _vigdisId,
            DataCategory.Visits,
            validTo: Now.AddMinutes(-1)
        );
        await SeedConsentAsync(_fabianId, _vigdisId, DataCategory.Visits);

        Assert.Empty(await ScopesOf(_fridaId));
    }

    [Fact]
    public async Task Scopes_ReturnAPairOnce_WhenARevokedRowSitsBesideAnOpenOne()
    {
        await SeedConsentAsync(
            _fridaId,
            _vigdisId,
            DataCategory.Visits,
            validFrom: Now.AddDays(-10),
            validTo: Now.AddDays(-5)
        );
        await SeedConsentAsync(_fridaId, _vigdisId, DataCategory.Visits);

        Assert.Single(await ScopesOf(_fridaId));
    }

    [Fact]
    public async Task Consented_ReturnsWhoHoldsBothAnOpenConsentAndAnOpenGrant()
    {
        await SeedGrantAsync(_fridaId, _vigdisId);
        await SeedConsentAsync(_fridaId, _vigdisId, DataCategory.Visits);
        await SeedConsentAsync(_fabianId, _vigdisId, DataCategory.Visits);

        Assert.Equal([_fridaId], await ConsentedTo(_vigdisId));
    }

    [Fact]
    public async Task Consented_ExcludesAClosedGrant()
    {
        await SeedGrantAsync(_fridaId, _vigdisId, validTo: Now.AddMinutes(-1));
        await SeedConsentAsync(_fridaId, _vigdisId, DataCategory.Visits);

        Assert.Empty(await ConsentedTo(_vigdisId));
    }

    [Fact]
    public async Task Consented_ExcludesAClosedConsent()
    {
        await SeedGrantAsync(_fridaId, _vigdisId);
        await SeedConsentAsync(
            _fridaId,
            _vigdisId,
            DataCategory.Visits,
            validTo: Now.AddMinutes(-1)
        );

        Assert.Empty(await ConsentedTo(_vigdisId));
    }

    [Fact]
    public async Task Consented_IsScopedToTheCategory_AndTheCareRecipient()
    {
        await SeedGrantAsync(_fridaId, _vigdisId);
        await SeedGrantAsync(_fridaId, _torId);
        await SeedConsentAsync(_fridaId, _vigdisId, DataCategory.Medications);
        await SeedConsentAsync(_fridaId, _torId, DataCategory.Visits);

        Assert.Empty(await ConsentedTo(_vigdisId, DataCategory.Visits));
        Assert.Equal([_fridaId], await ConsentedTo(_vigdisId, DataCategory.Medications));
        Assert.Equal([_fridaId], await ConsentedTo(_torId, DataCategory.Visits));
    }

    [Fact]
    public async Task Consented_RequiresTheGrantOnTheSameCareRecipient()
    {
        await SeedGrantAsync(_fridaId, _torId);
        await SeedConsentAsync(_fridaId, _vigdisId, DataCategory.Visits);

        Assert.Empty(await ConsentedTo(_vigdisId));
    }

    [Fact]
    public async Task Consented_EvaluatesBothWindows_AtTheInstantHandedIn()
    {
        await SeedGrantAsync(_fridaId, _vigdisId, validTo: Now.AddDays(1));
        await SeedConsentAsync(_fridaId, _vigdisId, DataCategory.Visits, validTo: Now.AddDays(2));

        Assert.Equal([_fridaId], await ConsentedTo(_vigdisId, asOf: Now));
        Assert.Empty(await ConsentedTo(_vigdisId, asOf: Now.AddDays(1).AddMinutes(1)));
    }

    [Fact]
    public async Task Consented_ReturnsAPersonOnce_WhenARevokedRowSitsBesideAnOpenOne()
    {
        await SeedGrantAsync(_fridaId, _vigdisId);
        await SeedConsentAsync(
            _fridaId,
            _vigdisId,
            DataCategory.Visits,
            validFrom: Now.AddDays(-10),
            validTo: Now.AddDays(-5)
        );
        await SeedConsentAsync(_fridaId, _vigdisId, DataCategory.Visits);

        Assert.Equal([_fridaId], await ConsentedTo(_vigdisId));
    }
}
