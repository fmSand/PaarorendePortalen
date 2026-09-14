using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Dtos;

public sealed record DayPlanResponse(
    int CareRecipientId,
    DateOnly Date,
    int CompletedCount,
    int OutstandingCount,
    IReadOnlyList<DayPlanItem> Items
);
