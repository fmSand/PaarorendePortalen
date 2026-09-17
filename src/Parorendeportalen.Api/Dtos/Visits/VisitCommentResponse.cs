using Parorendeportalen.Api.Models.Visits;

namespace Parorendeportalen.Api.Dtos.Visits;

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
