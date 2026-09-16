using System.ComponentModel.DataAnnotations;
using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Dtos;

public sealed record UpdateVisitRequest
{
    public required DateTimeOffset ScheduledAt { get; init; }

    [MaxLength(200)]
    public required string Title { get; init; }

    [MaxLength(2000)]
    public string? Notes { get; init; }

    public required Visibility Visibility { get; init; }
}
