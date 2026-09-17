using Parorendeportalen.Api.Dtos.Planning;

namespace Parorendeportalen.Api.Services.Planning;

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
