using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Models.Kinship;

namespace Parorendeportalen.Api.Repositories.Kinship;

public sealed class EfKinshipRegistry(AppDbContext context, TimeProvider timeProvider)
    : IKinshipRegistry
{
    public Task<NextOfKin?> GetByExternalIdAsync(
        string externalId,
        CancellationToken cancellationToken
    ) =>
        WithCurrentGrants().FirstOrDefaultAsync(n => n.ExternalId == externalId, cancellationToken);

    public Task<NextOfKin?> GetByNationalIdHashAsync(
        string nationalIdHash,
        CancellationToken cancellationToken
    ) =>
        WithCurrentGrants()
            .FirstOrDefaultAsync(n => n.NationalIdHash == nationalIdHash, cancellationToken);

    public async Task UpdateAsync(NextOfKin nextOfKin, CancellationToken cancellationToken)
    {
        // Only the person's own columns - Update() would mark the whole graph
        // modified, grants included
        context.NextOfKin.Attach(nextOfKin);
        context.Entry(nextOfKin).Property(n => n.ExternalId).IsModified = true;
        context.Entry(nextOfKin).Property(n => n.DisplayName).IsModified = true;
        await context.SaveChangesAsync(cancellationToken);
    }

    // Validity lives on the grant, so an existing person is always returned,
    // only their currently-open grants come along
    private IQueryable<NextOfKin> WithCurrentGrants()
    {
        var now = timeProvider.GetUtcNow();

        return context
            .NextOfKin.AsNoTracking()
            .Include(n =>
                n.Grants.Where(g => g.ValidFrom <= now && (g.ValidTo == null || g.ValidTo > now))
            )
                .ThenInclude(g => g.CareRecipient);
    }
}
