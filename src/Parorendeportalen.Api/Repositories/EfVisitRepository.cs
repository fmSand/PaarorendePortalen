using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Repositories;

public sealed class EfVisitRepository(AppDbContext context) : IVisitRepository
{
    public async Task<(IReadOnlyList<Visit> Items, int TotalCount)> GetByCareRecipientIdAsync(
        int careRecipientId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var allForCareRecipient = await context.Visits
            .AsNoTracking()
            .Include(v => v.CareRecipient)
            .Where(v => v.CareRecipientId == careRecipientId)
            .ToListAsync(cancellationToken);

        var filtered = allForCareRecipient
            .Where(v => from is null || v.ScheduledAt >= from)
            .Where(v => to is null || v.ScheduledAt <= to)
            .OrderBy(v => v.ScheduledAt)
            .ToList();

        var items = filtered
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return (items, filtered.Count);
    }

    public async Task<Visit?> GetByIdAsync(int id, int careRecipientId, CancellationToken cancellationToken)
    {
        return await context.Visits
            .AsNoTracking()
            .Include(v => v.CareRecipient)
            .FirstOrDefaultAsync(
                v => v.Id == id && v.CareRecipientId == careRecipientId,
                cancellationToken);
    }
}
