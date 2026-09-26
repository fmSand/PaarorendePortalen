using Parorendeportalen.Api.Models.Kinship;

namespace Parorendeportalen.Api.Models.Visits;

public class VisitComment
{
    public int Id { get; set; }

    public int VisitId { get; set; }

    public Visit Visit { get; set; } = null!;

    public int AuthorNextOfKinId { get; set; }

    public NextOfKin Author { get; set; } = null!;

    public required string Body { get; set; }

    public Visibility Visibility { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public uint Version { get; set; }

    // EfVisitCommentRepository.GetByVisitIdAsync states the same rule in SQL.
    public bool IsVisibleTo(int nextOfKinId) =>
        Visibility == Visibility.Shared || AuthorNextOfKinId == nextOfKinId;
}
