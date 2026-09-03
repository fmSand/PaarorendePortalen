using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Repositories;

public sealed class EfCareRecipientRepository(AppDbContext context) : ICareRecipientRepository
{
    public async Task<IReadOnlyList<CareRecipient>> GetByIdsAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken
    )
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await context
            .CareRecipients.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<CareRecipient?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await context
            .CareRecipients.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyDictionary<string, int>> GetIdsByNationalIdHashesAsync(
        IReadOnlyCollection<string> nationalIdHashes,
        CancellationToken cancellationToken
    )
    {
        if (nationalIdHashes.Count == 0)
        {
            return ReadOnlyDictionary<string, int>.Empty;
        }

        var matches = await context
            .CareRecipients.AsNoTracking()
            .Where(c => c.NationalIdHash != null && nationalIdHashes.Contains(c.NationalIdHash))
            .Select(c => new { Hash = c.NationalIdHash!, c.Id })
            .ToListAsync(cancellationToken);

        return matches.ToDictionary(match => match.Hash, match => match.Id, StringComparer.Ordinal);
    }
}
