using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Models.Planning;

namespace Parorendeportalen.Api.Repositories.Planning;

public sealed class EfVedtakRepository(AppDbContext context) : IVedtakRepository
{
    public async Task<IReadOnlyList<Vedtak>> GetByCareRecipientIdAsync(
        int careRecipientId,
        CancellationToken cancellationToken
    )
    {
        return await Scoped(careRecipientId)
            // The (VedtakId, Sequence) index usually returns tasks in order anyway,
            // which is why dropping this ordering still passes the test for it. Not a guarantee.
            .Include(v => v.Tasks.OrderBy(task => task.Sequence).ThenBy(task => task.Id))
            .OrderByDescending(v => v.ValidFrom)
            .ThenBy(v => v.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<Vedtak?> GetByIdAsync(
        int id,
        int careRecipientId,
        CancellationToken cancellationToken
    )
    {
        // Scoped by both, so someone else's id gets the same 404 as one that doesn't exist.
        return await Scoped(careRecipientId)
            .Include(v => v.Tasks.OrderBy(task => task.Sequence).ThenBy(task => task.Id))
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Vedtak>> GetInForceOnAsync(
        int careRecipientId,
        DateOnly date,
        CancellationToken cancellationToken
    )
    {
        // No Include: the day plan expands the recurrence rule and never reads the task list.
        return await Scoped(careRecipientId)
            .Where(v =>
                v.Status == VedtakStatus.Active
                && v.ValidFrom <= date
                && (v.ValidTo == null || date <= v.ValidTo)
            )
            .OrderBy(v => v.ServiceType)
            .ThenBy(v => v.Id)
            .ToListAsync(cancellationToken);
    }

    private IQueryable<Vedtak> Scoped(int careRecipientId) =>
        context.Vedtak.AsNoTracking().Where(v => v.CareRecipientId == careRecipientId);
}
