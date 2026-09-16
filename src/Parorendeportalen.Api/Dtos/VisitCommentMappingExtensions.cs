using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Dtos;

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
