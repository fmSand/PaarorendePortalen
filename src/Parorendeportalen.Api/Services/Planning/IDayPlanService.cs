using Parorendeportalen.Api.Dtos.Planning;

namespace Parorendeportalen.Api.Services.Planning;

public interface IDayPlanService
{
    Task<DayPlanResponse> GetAsync(
        int careRecipientId,
        DateOnly date,
        CancellationToken cancellationToken
    );
}
