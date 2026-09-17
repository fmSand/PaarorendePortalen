using System.ComponentModel.DataAnnotations;
using Parorendeportalen.Api.Models.Visits;

namespace Parorendeportalen.Api.Dtos.Visits;

public sealed record UpdateVisitCommentRequest
{
    [MinLength(1)]
    [MaxLength(2000)]
    public required string Body { get; init; }

    public required Visibility Visibility { get; init; }
}
