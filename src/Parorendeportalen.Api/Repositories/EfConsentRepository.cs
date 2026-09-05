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
        await OpenAt(asOf)
            .Where(consent =>
                consent.NextOfKinId == nextOfKinId && consent.CareRecipientId == careRecipientId
            )
            .Select(consent => consent.Category)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ConsentScope>> GetActiveScopesAsync(
        int nextOfKinId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken
    )
    {
        var pairs = await OpenAt(asOf)
            .Where(consent => consent.NextOfKinId == nextOfKinId)
            .Select(consent => new { consent.CareRecipientId, consent.Category })
            .Distinct()
            .ToListAsync(cancellationToken);

        return pairs.Select(pair => new ConsentScope(pair.CareRecipientId, pair.Category)).ToList();
    }

    public async Task<IReadOnlyList<int>> GetConsentedNextOfKinIdsAsync(
        int careRecipientId,
        DataCategory category,
        DateTimeOffset asOf,
        CancellationToken cancellationToken
    ) =>
        await OpenAt(asOf)
            .Where(consent =>
                consent.CareRecipientId == careRecipientId && consent.Category == category
            )
            .Where(consent =>
                context.KinshipGrants.Any(grant =>
                    grant.NextOfKinId == consent.NextOfKinId
                    && grant.CareRecipientId == careRecipientId
                    && grant.ValidFrom <= asOf
                    && (grant.ValidTo == null || grant.ValidTo > asOf)
                )
            )
            .Select(consent => consent.NextOfKinId)
            .Distinct()
            .ToListAsync(cancellationToken);

    private IQueryable<Consent> OpenAt(DateTimeOffset asOf) =>
        context
            .Consents.AsNoTracking()
            .Where(consent =>
                consent.ValidFrom <= asOf && (consent.ValidTo == null || consent.ValidTo > asOf)
            );
}
