using Parorendeportalen.Api.Models.Access;

namespace Parorendeportalen.Api.Repositories.Access;

// Append only. No read, update or delete: a next-of-kin must not see the log
// (fullmakt asymmetry), and a log a caller can edit is not a log.
public interface IAccessLogRepository
{
    Task AppendAsync(AccessLogEntry entry, CancellationToken cancellationToken);
}
