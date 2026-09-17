using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Parorendeportalen.Api.Controllers;
using Parorendeportalen.Api.Dtos.Visits;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Models.Access;
using Parorendeportalen.Api.Models.Visits;
using Parorendeportalen.Api.Services;
using Parorendeportalen.Api.Services.Access;
using Parorendeportalen.Api.Services.Visits;

namespace Parorendeportalen.Api.Tests.Controllers;

public class VisitCommentsControllerTests
{
    private const int VisitId = 12;
    private const int CareRecipientId = 7;
    private const int DeniedCareRecipientId = 9;

    private readonly IVisitCommentService _commentService = Substitute.For<IVisitCommentService>();
    private readonly IHealthDataAccessPolicy _accessPolicy =
        Substitute.For<IHealthDataAccessPolicy>();
    private readonly VisitCommentsController _sut;

    public VisitCommentsControllerTests()
    {
        _accessPolicy
            .AuthorizeReadAsync(
                CareRecipientId,
                Arg.Any<DataCategory>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(AccessDecision.Granted);
        _accessPolicy
            .AuthorizeWriteAsync(
                CareRecipientId,
                Arg.Any<DataCategory>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(AccessDecision.Granted);
        _accessPolicy
            .AuthorizeReadAsync(
                DeniedCareRecipientId,
                Arg.Any<DataCategory>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(AccessDecision.DeniedNoConsent);

        _sut = new VisitCommentsController(_commentService, _accessPolicy)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Fact]
    public async Task Get_Returns400_WhenNoCareRecipientScopeIsGiven()
    {
        var result = await _sut.Get(VisitId, careRecipientId: null, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        var problem = Assert.IsType<ValidationProblemDetails>(objectResult.Value);
        // The status code comes from the running app's factory; a bare controller only names the field.
        Assert.True(problem.Errors.ContainsKey("careRecipientId"));
        await _commentService
            .DidNotReceive()
            .GetByVisitIdAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_Returns403_WhenTheCategoryWasNeverConsentedTo()
    {
        var result = await _sut.Get(VisitId, DeniedCareRecipientId, CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task Get_Returns404_WhenTheVisitIsNotOneTheCallerCanSee()
    {
        _commentService
            .GetByVisitIdAsync(VisitId, CareRecipientId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<VisitCommentResponse>?)null);

        var result = await _sut.Get(VisitId, CareRecipientId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Get_ReturnsTheThread_WhenTheVisitIsVisible()
    {
        _commentService
            .GetByVisitIdAsync(VisitId, CareRecipientId, Arg.Any<CancellationToken>())
            .Returns([AComment()]);

        var result = await _sut.Get(VisitId, CareRecipientId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var thread = Assert.IsAssignableFrom<IReadOnlyList<VisitCommentResponse>>(ok.Value);
        Assert.Equal("Husk resepten", Assert.Single(thread).Body);
    }

    [Fact]
    public async Task Create_Returns201WithAnETag()
    {
        _commentService
            .CreateAsync(
                VisitId,
                CareRecipientId,
                Arg.Any<CreateVisitCommentRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(WriteResult<VisitCommentResponse>.Succeeded(AComment()));

        var result = await _sut.Create(
            VisitId,
            CareRecipientId,
            new CreateVisitCommentRequest
            {
                Body = "Husk resepten",
                Visibility = Visibility.Shared,
            },
            CancellationToken.None
        );

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal("\"77\"", _sut.Response.Headers.ETag);
    }

    [Fact]
    public async Task Delete_Returns428_WhenIfMatchIsAbsent()
    {
        var result = await _sut.Delete(VisitId, 11, CareRecipientId, CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status428PreconditionRequired, problem.StatusCode);
        await _commentService
            .DidNotReceive()
            .DeleteAsync(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<uint>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Delete_Returns204_WhenTheCommentIsGone()
    {
        _sut.Request.Headers.IfMatch = "\"77\"";
        _commentService
            .DeleteAsync(11, VisitId, CareRecipientId, 77u, Arg.Any<CancellationToken>())
            .Returns(WriteOutcome.Succeeded);

        var result = await _sut.Delete(VisitId, 11, CareRecipientId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    private static VisitCommentResponse AComment() =>
        new(
            11,
            VisitId,
            4,
            "Fabian Quist",
            "Husk resepten",
            Visibility.Shared,
            new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero),
            null,
            77
        );
}
