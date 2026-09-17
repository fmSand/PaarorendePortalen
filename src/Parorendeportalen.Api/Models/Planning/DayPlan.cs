namespace Parorendeportalen.Api.Models.Planning;

// Has no table. Computed from the vedtak and the visits each time, so the same
// date gives the same answer.
public sealed record DayPlan(int CareRecipientId, DateOnly Date, IReadOnlyList<DayPlanItem> Items)
{
    public int CompletedCount => Items.Count(item => item.Status == DayPlanItemStatus.Completed);

    public int OutstandingCount =>
        Items.Count(item => item.Status is DayPlanItemStatus.Expected or DayPlanItemStatus.Planned);
}

// VedtakId is null for a visit the municipality made that no vedtak in force accounts for.
public sealed record DayPlanItem(
    ServiceType ServiceType,
    string? Title,
    int Occurrence,
    DayPlanItemStatus Status,
    int? VedtakId,
    int? VisitId,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? ActualAt,
    string? CaregiverName
);
