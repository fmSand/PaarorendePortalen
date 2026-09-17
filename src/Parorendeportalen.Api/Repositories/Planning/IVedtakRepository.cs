using Parorendeportalen.Api.Models.Planning;

namespace Parorendeportalen.Api.Repositories.Planning;

public interface IVedtakRepository
{
    // Every vedtak the person has, in force or not, so a next-of-kin can see that one was revoked.
    Task<IReadOnlyList<Vedtak>> GetByCareRecipientIdAsync(
        int careRecipientId,
        CancellationToken cancellationToken
    );

    Task<Vedtak?> GetByIdAsync(int id, int careRecipientId, CancellationToken cancellationToken);

    // Vedtak.IsInForceOn's rule, stated in SQL; EfVedtakRepositoryTests holds the two to each other.
    Task<IReadOnlyList<Vedtak>> GetInForceOnAsync(
        int careRecipientId,
        DateOnly date,
        CancellationToken cancellationToken
    );
}
