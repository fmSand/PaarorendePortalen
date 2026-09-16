using System.ComponentModel.DataAnnotations;
using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Dtos;

public sealed record UpdateVisitCommentRequest
{
    [MinLength(1)]
    [MaxLength(2000)]
    public required string Body { get; init; }

    public required Visibility Visibility { get; init; }
}
