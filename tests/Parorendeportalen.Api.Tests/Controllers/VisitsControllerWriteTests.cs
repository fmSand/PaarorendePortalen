using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Parorendeportalen.Api.Controllers;
using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Tests.Controllers;

// HTTP half of the write path: If-Match, outcome-to-status-code mapping, and the returned ETag.
public class VisitsControllerWriteTests
{
    private const int CareRecipientId = 7;
    private const int DeniedCareRecipientId = 9;

    private readonly IVisitService _visitService = Substitute.For<IVisitService>();
    private readonly IHealthDataAccessPolicy _accessPolicy =
        Substitute.For<IHealthDataAccessPolicy>();
    private readonly VisitsController _sut;

    public VisitsControllerWriteTests()
    {
        _accessPolicy
            .AuthorizeWriteAsync(
                CareRecipientId,
                Arg.Any<DataCategory>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(AccessDecision.Granted);
        _accessPolicy
            .AuthorizeWriteAsync(
                DeniedCareRecipientId,
                Arg.Any<DataCategory>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(AccessDecision.DeniedNoConsent);

        _sut = new VisitsController(_visitService, _accessPolicy)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Fact]
    public async Task Create_Returns201WithALocationAndAnETag()
    {
        _visitService
            .CreateAsync(Arg.Any<CreateVisitRequest>(), Arg.Any<CancellationToken>())
            .Returns(AVisitResponse(version: 4242));

        var result = await _sut.Create(ACreateRequest(), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(VisitsController.GetById), created.ActionName);
        Assert.Equal(12, created.RouteValues!["id"]);
        Assert.Equal(CareRecipientId, created.RouteValues["careRecipientId"]);
        Assert.Equal("\"4242\"", _sut.Response.Headers.ETag);
    }

    [Fact]
    public async Task Create_IsRefusedBeforeTheServiceRuns_WhenConsentIsMissing()
    {
        var result = await _sut.Create(
            ACreateRequest(DeniedCareRecipientId),
            CancellationToken.None
        );

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        await _visitService
            .DidNotReceive()
            .CreateAsync(Arg.Any<CreateVisitRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_Returns428_WhenIfMatchIsAbsent()
    {
        var result = await _sut.Update(
            12,
            CareRecipientId,
            AnUpdateRequest(),
            CancellationToken.None
        );

        AssertProblem(result.Result, StatusCodes.Status428PreconditionRequired);
        await _visitService
            .DidNotReceive()
            .UpdateAsync(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<UpdateVisitRequest>(),
                Arg.Any<uint>(),
                Arg.Any<CancellationToken>()
            );
    }

    // Weak tags and "*" must both be rejected by a lost-update guard.
    [Theory]
    [InlineData("*")]
    [InlineData("W/\"41\"")]
    [InlineData("\"not-a-version\"")]
    [InlineData("41")]
    public async Task Update_Returns400_WhenIfMatchIsNotAVersionThisApiIssued(string header)
    {
        GivenIfMatch(header);

        var result = await _sut.Update(
            12,
            CareRecipientId,
            AnUpdateRequest(),
            CancellationToken.None
        );

        AssertProblem(result.Result, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Update_PassesTheStatedVersionThrough_AndReturnsTheNewETag()
    {
        GivenIfMatch("\"41\"");
        _visitService
            .UpdateAsync(
                12,
                CareRecipientId,
                Arg.Any<UpdateVisitRequest>(),
                41u,
                Arg.Any<CancellationToken>()
            )
            .Returns(WriteResult<VisitResponse>.Succeeded(AVisitResponse(version: 42)));

        var result = await _sut.Update(
            12,
            CareRecipientId,
            AnUpdateRequest(),
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("\"42\"", _sut.Response.Headers.ETag);
    }

    [Theory]
    [InlineData(WriteOutcome.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(WriteOutcome.NotAuthor, StatusCodes.Status403Forbidden)]
    [InlineData(WriteOutcome.SourceOwned, StatusCodes.Status403Forbidden)]
    [InlineData(WriteOutcome.VersionConflict, StatusCodes.Status412PreconditionFailed)]
    public async Task Update_MapsEachRefusalToItsOwnStatusCode(
        WriteOutcome outcome,
        int expectedStatus
    )
    {
        GivenIfMatch("\"41\"");
        _visitService
            .UpdateAsync(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<UpdateVisitRequest>(),
                Arg.Any<uint>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(WriteResult<VisitResponse>.Failed(outcome));

        var result = await _sut.Update(
            12,
            CareRecipientId,
            AnUpdateRequest(),
            CancellationToken.None
        );

        if (expectedStatus == StatusCodes.Status404NotFound)
        {
            Assert.IsType<NotFoundResult>(result.Result);
            return;
        }

        AssertProblem(result.Result, expectedStatus);
    }

    [Fact]
    public async Task Delete_Returns204_WhenTheEntryIsGone()
    {
        GivenIfMatch("\"41\"");
        _visitService
            .DeleteAsync(12, CareRecipientId, 41u, Arg.Any<CancellationToken>())
            .Returns(WriteOutcome.Succeeded);

        var result = await _sut.Delete(12, CareRecipientId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_Returns412_WhenSomeoneElseChangedTheEntryFirst()
    {
        GivenIfMatch("\"41\"");
        _visitService
            .DeleteAsync(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<uint>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(WriteOutcome.VersionConflict);

        var result = await _sut.Delete(12, CareRecipientId, CancellationToken.None);

        AssertProblem(result, StatusCodes.Status412PreconditionFailed);
    }

    private void GivenIfMatch(string value) => _sut.Request.Headers.IfMatch = value;

    private static void AssertProblem(IActionResult? result, int expectedStatus)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(expectedStatus, problem.Status);
    }

    private static CreateVisitRequest ACreateRequest(int careRecipientId = CareRecipientId) =>
        new()
        {
            CareRecipientId = careRecipientId,
            ScheduledAt = new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero),
            Title = "Legetime",
            Visibility = Visibility.Shared,
        };

    private static UpdateVisitRequest AnUpdateRequest() =>
        new()
        {
            ScheduledAt = new DateTimeOffset(2026, 9, 21, 11, 0, 0, TimeSpan.Zero),
            Title = "Flyttet legetime",
            Visibility = Visibility.Shared,
        };

    private static VisitResponse AVisitResponse(uint version) =>
        new(
            12,
            CareRecipientId,
            "Vigdis Quist",
            new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero),
            null,
            VisitStatus.Planned,
            null,
            null,
            null,
            "Legetime",
            Origin.Portal,
            4,
            "Fabian Quist",
            Visibility.Shared,
            new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero),
            null,
            version
        );
}
