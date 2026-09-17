using Parorendeportalen.Api.Models.Planning;

namespace Parorendeportalen.Api.Dtos.Planning;

public sealed record DayPlanResponse(
    int CareRecipientId,
    DateOnly Date,
    int CompletedCount,
    int OutstandingCount,
    IReadOnlyList<DayPlanItem> Items
);
