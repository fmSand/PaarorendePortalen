using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Repositories;

namespace Parorendeportalen.Api.Services;

// Fetches the two halves and lets DayPlanBuilder decide. No vedtak and no visits is
// an empty plan, not a 404: the person and the day both exist, "nothing scheduled" is the answer.
public sealed class DayPlanService(IVedtakRepository vedtak, IVisitRepository visits)
    : IDayPlanService
{
    public async Task<DayPlanResponse> GetAsync(
        int careRecipientId,
        DateOnly date,
        CancellationToken cancellationToken
    )
    {
        var (start, end) = NorwegianTime.BoundsOf(date);

        var inForce = await vedtak.GetInForceOnAsync(careRecipientId, date, cancellationToken);
        var reported = await visits.GetInRangeAsync(careRecipientId, start, end, cancellationToken);

        return DayPlanBuilder.Build(careRecipientId, date, inForce, reported).ToResponse();
    }
}
