using System.ComponentModel.DataAnnotations;
using Parorendeportalen.Api.Models.Visits;

namespace Parorendeportalen.Api.Dtos.Visits;

public sealed record CreateVisitRequest
{
    public required int CareRecipientId { get; init; }

    public required DateTimeOffset ScheduledAt { get; init; }

    [MaxLength(200)]
    public required string Title { get; init; }

    [MaxLength(2000)]
    public string? Notes { get; init; }

    public required Visibility Visibility { get; init; }
}
