using Parorendeportalen.Api.Dtos;

namespace Parorendeportalen.Api.Services;

public interface IDayPlanService
{
    Task<DayPlanResponse> GetAsync(
        int careRecipientId,
        DateOnly date,
        CancellationToken cancellationToken
    );
}
