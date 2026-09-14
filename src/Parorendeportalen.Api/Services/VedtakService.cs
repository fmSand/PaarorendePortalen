using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Repositories;

namespace Parorendeportalen.Api.Services;

public sealed class VedtakService(IVedtakRepository repository) : IVedtakService
{
    public async Task<IReadOnlyList<VedtakResponse>> GetByCareRecipientIdAsync(
        int careRecipientId,
        CancellationToken cancellationToken
    )
    {
        var vedtak = await repository.GetByCareRecipientIdAsync(careRecipientId, cancellationToken);

        return [.. vedtak.Select(v => v.ToResponse())];
    }

    public async Task<VedtakResponse?> GetByIdAsync(
        int id,
        int careRecipientId,
        CancellationToken cancellationToken
    )
    {
        var vedtak = await repository.GetByIdAsync(id, careRecipientId, cancellationToken);

        return vedtak?.ToResponse();
    }
}
