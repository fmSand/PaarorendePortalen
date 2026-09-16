using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Dtos;

public sealed record VisitCommentResponse(
    int Id,
    int VisitId,
    int AuthorNextOfKinId,
    string AuthorName,
    string Body,
    Visibility Visibility,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    uint Version
);
