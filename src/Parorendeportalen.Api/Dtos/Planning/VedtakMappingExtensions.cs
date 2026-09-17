using Parorendeportalen.Api.Models.Planning;

namespace Parorendeportalen.Api.Dtos.Planning;

public static class VedtakMappingExtensions
{
    public static VedtakResponse ToResponse(this Vedtak vedtak) =>
        new(
            vedtak.Id,
            vedtak.CareRecipientId,
            vedtak.ServiceType,
            vedtak.Title,
            new RecurrenceResponse(vedtak.Recurrence.Days.ToDays(), vedtak.Recurrence.TimesPerDay),
            vedtak.ValidFrom,
            vedtak.ValidTo,
            vedtak.Status,
            [.. vedtak.Tasks.Select(task => task.ToResponse())]
        );

    public static VedtakTaskResponse ToResponse(this VedtakTask task) =>
        new(task.Id, task.Description, task.Sequence);

    public static DayPlanResponse ToResponse(this DayPlan plan) =>
        new(
            plan.CareRecipientId,
            plan.Date,
            plan.CompletedCount,
            plan.OutstandingCount,
            plan.Items
        );
}
