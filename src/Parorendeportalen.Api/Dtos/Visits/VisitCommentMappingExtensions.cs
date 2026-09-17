using Parorendeportalen.Api.Models.Visits;

namespace Parorendeportalen.Api.Dtos.Visits;

public static class VisitCommentMappingExtensions
{
    public static VisitCommentResponse ToResponse(this VisitComment comment) =>
        new(
            comment.Id,
            comment.VisitId,
            comment.AuthorNextOfKinId,
            comment.Author.DisplayName,
            comment.Body,
            comment.Visibility,
            comment.CreatedAt,
            comment.UpdatedAt,
            comment.Version
        );
}
