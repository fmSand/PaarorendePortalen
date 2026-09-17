using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Models.Access;

namespace Parorendeportalen.Api.Repositories.Access;

public sealed class EfAccessLogRepository(AppDbContext context) : IAccessLogRepository
{
    public async Task AppendAsync(AccessLogEntry entry, CancellationToken cancellationToken)
    {
        context.AccessLogEntries.Add(entry);
        await context.SaveChangesAsync(cancellationToken);
    }
}
