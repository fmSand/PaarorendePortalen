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
}
