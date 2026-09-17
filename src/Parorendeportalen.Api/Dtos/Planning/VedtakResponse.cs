using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Models.Planning;

namespace Parorendeportalen.Api.Dtos.Planning;

public sealed record VedtakResponse(
    int Id,
    int CareRecipientId,
    ServiceType ServiceType,
    string Title,
    RecurrenceResponse Recurrence,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    VedtakStatus Status,
    IReadOnlyList<VedtakTaskResponse> Tasks
);

public sealed record RecurrenceResponse(IReadOnlyList<DayOfWeek> Days, int TimesPerDay);

public sealed record VedtakTaskResponse(int Id, string Description, int Sequence);
