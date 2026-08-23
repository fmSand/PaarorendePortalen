using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Repositories;

public sealed class EfKinshipRegistry(AppDbContext context) : IKinshipRegistry
{
    public async Task<NextOfKin?> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken)
    {
        var nextOfKin = await context.NextOfKin
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.ExternalId == externalId, cancellationToken);
        return AsCurrentlyValid(nextOfKin);
    }

    public async Task<NextOfKin?> GetByNationalIdHashAsync(string nationalIdHash, CancellationToken cancellationToken)
    {
        var nextOfKin = await context.NextOfKin
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.NationalIdHash == nationalIdHash, cancellationToken);
        return AsCurrentlyValid(nextOfKin);
    }

    public async Task UpdateAsync(NextOfKin nextOfKin, CancellationToken cancellationToken)
    {
        context.NextOfKin.Update(nextOfKin);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static NextOfKin? AsCurrentlyValid(NextOfKin? nextOfKin) =>
        nextOfKin is not null && (nextOfKin.ValidTo is null || nextOfKin.ValidTo > DateTimeOffset.UtcNow)
            ? nextOfKin
            : null;
}
