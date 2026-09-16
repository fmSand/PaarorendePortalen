using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Repositories;

namespace Parorendeportalen.Api.Services;

// Fetches the two halves and lets DayPlanBuilder decide
public sealed class DayPlanService(
    IVedtakRepository vedtak,
    IVisitRepository visits,
    ICurrentNextOfKinAccessor currentNextOfKin
) : IDayPlanService
{
    public async Task<DayPlanResponse> GetAsync(
        int careRecipientId,
        DateOnly date,
        CancellationToken cancellationToken
    )
    {
        var (start, end) = NorwegianTime.BoundsOf(date);

        var current =
            await currentNextOfKin.GetCurrentAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "The session resolves to no next-of-kin, so no day plan can be built for it."
            );

        var inForce = await vedtak.GetInForceOnAsync(careRecipientId, date, cancellationToken);

        // Viewer-scoped here, even though a portal entry never settles an occurrence.
        var reported = await visits.GetInRangeAsync(
            careRecipientId,
            current.NextOfKinId,
            start,
            end,
            cancellationToken
        );

        return DayPlanBuilder.Build(careRecipientId, date, inForce, reported).ToResponse();
    }
}
