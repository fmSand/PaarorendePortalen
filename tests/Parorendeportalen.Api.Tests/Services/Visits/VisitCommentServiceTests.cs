using NSubstitute;
using Parorendeportalen.Api.Dtos.Visits;
using Parorendeportalen.Api.Models.Kinship;
using Parorendeportalen.Api.Models.Visits;
using Parorendeportalen.Api.Repositories.Visits;
using Parorendeportalen.Api.Services;
using Parorendeportalen.Api.Services.Kinship;
using Parorendeportalen.Api.Services.Visits;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Services.Visits;

public class VisitCommentServiceTests
{
    private const int Viewer = 7;
    private const int VisitId = 5;
    private const int CareRecipientId = 1;

    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    private readonly IVisitCommentRepository _comments = Substitute.For<IVisitCommentRepository>();
    private readonly IVisitRepository _visits = Substitute.For<IVisitRepository>();
    private readonly ICurrentNextOfKinAccessor _currentNextOfKin =
        Substitute.For<ICurrentNextOfKinAccessor>();
    private readonly VisitCommentService _sut;

    public VisitCommentServiceTests()
    {
        _currentNextOfKin
            .GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(new CurrentNextOfKin(Viewer, [CareRecipientId]));

        GivenTheVisitIsVisible();

        _sut = new VisitCommentService(
            _comments,
            _visits,
            _currentNextOfKin,
            new FixedTimeProvider(Now)
        );
    }

    [Fact]
    public async Task GetByVisitIdAsync_ReturnsNull_WhenTheVisitIsNotOneTheCallerCanSee()
    {
        GivenTheVisitIsNotVisible();

        var thread = await _sut.GetByVisitIdAsync(VisitId, CareRecipientId, CancellationToken.None);

        // Null means the visit is hidden (404). Empty list means visit with no comments.
        Assert.Null(thread);
        await _comments
            .DidNotReceive()
            .GetByVisitIdAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_RefusesWhenTheVisitIsNotOneTheCallerCanSee()
    {
        GivenTheVisitIsNotVisible();

        var result = await _sut.CreateAsync(
            VisitId,
            CareRecipientId,
            new CreateVisitCommentRequest { Body = "Hei", Visibility = Visibility.Shared },
            CancellationToken.None
        );

        Assert.Equal(WriteOutcome.NotFound, result.Outcome);
        await _comments
            .DidNotReceive()
            .AddAsync(Arg.Any<VisitComment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_StampsTheCommentWithItsAuthorAndTime()
    {
        VisitComment? saved = null;
        _comments
            .AddAsync(Arg.Any<VisitComment>(), Arg.Any<CancellationToken>())
            .Returns(call => saved = call.Arg<VisitComment>());
        _comments
            .GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => WithAuthor(saved!));

        var result = await _sut.CreateAsync(
            VisitId,
            CareRecipientId,
            new CreateVisitCommentRequest
            {
                Body = "Husk å ta med resepten",
                Visibility = Visibility.Shared,
            },
            CancellationToken.None
        );

        Assert.Equal(WriteOutcome.Succeeded, result.Outcome);
        Assert.NotNull(saved);
        Assert.Equal(Viewer, saved.AuthorNextOfKinId);
        Assert.Equal(VisitId, saved.VisitId);
        Assert.Equal(Visibility.Shared, saved.Visibility);
        Assert.Equal(Now, saved.CreatedAt);
        Assert.Null(saved.UpdatedAt);
    }

    [Theory]
    [InlineData(99, Visibility.Shared, WriteOutcome.NotAuthor)]
    [InlineData(99, Visibility.Private, WriteOutcome.NotFound)]
    [InlineData(99, (Visibility)7, WriteOutcome.NotFound)]
    public async Task UpdateAsync_RefusesAnotherNextOfKinsComment(
        int author,
        Visibility visibility,
        WriteOutcome expected
    )
    {
        GivenRepositoryHolds(AComment(author, visibility));

        var result = await _sut.UpdateAsync(
            11,
            VisitId,
            CareRecipientId,
            AnUpdate(),
            1u,
            CancellationToken.None
        );

        Assert.Equal(expected, result.Outcome);
        await _comments
            .DidNotReceive()
            .UpdateAsync(Arg.Any<VisitComment>(), Arg.Any<uint>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_ReturnsVersionConflict_WhenTheCommentMovedOn()
    {
        GivenRepositoryHolds(AComment(Viewer, Visibility.Shared));
        _comments
            .UpdateAsync(Arg.Any<VisitComment>(), Arg.Any<uint>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.UpdateAsync(
            11,
            VisitId,
            CareRecipientId,
            AnUpdate(),
            41u,
            CancellationToken.None
        );

        Assert.Equal(WriteOutcome.VersionConflict, result.Outcome);
    }

    [Fact]
    public async Task UpdateAsync_EditsTheCallersOwnComment_AndPassesTheStatedVersion()
    {
        var comment = AComment(Viewer, Visibility.Shared);
        GivenRepositoryHolds(comment);
        _comments
            .UpdateAsync(Arg.Any<VisitComment>(), Arg.Any<uint>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _comments.GetByIdAsync(11, Arg.Any<CancellationToken>()).Returns(_ => WithAuthor(comment));

        var result = await _sut.UpdateAsync(
            11,
            VisitId,
            CareRecipientId,
            new UpdateVisitCommentRequest { Body = "Rettet", Visibility = Visibility.Private },
            41u,
            CancellationToken.None
        );

        Assert.Equal(WriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("Rettet", comment.Body);
        Assert.Equal(Visibility.Private, comment.Visibility);
        Assert.Equal(Now, comment.UpdatedAt);
        await _comments.Received(1).UpdateAsync(comment, 41u, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheCallersOwnComment()
    {
        var comment = AComment(Viewer, Visibility.Private);
        GivenRepositoryHolds(comment);
        _comments
            .DeleteAsync(Arg.Any<VisitComment>(), Arg.Any<uint>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var outcome = await _sut.DeleteAsync(
            11,
            VisitId,
            CareRecipientId,
            41u,
            CancellationToken.None
        );

        Assert.Equal(WriteOutcome.Succeeded, outcome);
        await _comments.Received(1).DeleteAsync(comment, 41u, Arg.Any<CancellationToken>());
    }

    private void GivenTheVisitIsVisible() =>
        _visits
            .GetByIdAsync(VisitId, CareRecipientId, Viewer, Arg.Any<CancellationToken>())
            .Returns(new Visit { Id = VisitId, CareRecipientId = CareRecipientId });

    private void GivenTheVisitIsNotVisible() =>
        _visits
            .GetByIdAsync(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns((Visit?)null);

    private void GivenRepositoryHolds(VisitComment? comment) =>
        _comments
            .GetForWriteAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(comment);

    private static UpdateVisitCommentRequest AnUpdate() =>
        new() { Body = "Rettet", Visibility = Visibility.Shared };

    private static VisitComment AComment(int author, Visibility visibility) =>
        new()
        {
            Id = 11,
            VisitId = VisitId,
            AuthorNextOfKinId = author,
            Body = "Opprinnelig",
            Visibility = visibility,
            CreatedAt = Now.AddDays(-1),
        };

    private static VisitComment WithAuthor(VisitComment comment)
    {
        comment.Author = new NextOfKin
        {
            Id = comment.AuthorNextOfKinId,
            NationalIdHash = new string('a', 64),
            DisplayName = "Fabian Quist",
        };
        return comment;
    }
}
