using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Repositories;

public sealed class EfConsentRepository(AppDbContext context) : IConsentRepository
{
    public async Task<IReadOnlyList<DataCategory>> GetActiveCategoriesAsync(
        int nextOfKinId,
        int careRecipientId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken
    ) =>
        await context
            .Consents.AsNoTracking()
            .Where(consent =>
                consent.NextOfKinId == nextOfKinId
                && consent.CareRecipientId == careRecipientId
                && consent.ValidFrom <= asOf
                && (consent.ValidTo == null || consent.ValidTo > asOf)
            )
            .Select(consent => consent.Category)
            .Distinct()
            .ToListAsync(cancellationToken);
}
