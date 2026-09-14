using Parorendeportalen.Api.Dtos;

namespace Parorendeportalen.Api.Services;

public interface IVedtakService
{
    Task<IReadOnlyList<VedtakResponse>> GetByCareRecipientIdAsync(
        int careRecipientId,
        CancellationToken cancellationToken
    );

    Task<VedtakResponse?> GetByIdAsync(
        int id,
        int careRecipientId,
        CancellationToken cancellationToken
    );
}
