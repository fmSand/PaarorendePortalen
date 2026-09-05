using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Parorendeportalen.Api.Controllers;
using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Tests.Controllers;

public class NotificationsControllerTests
{
    private const int NextOfKinId = 5;
    private const int CareRecipientId = 7;

    private static readonly IReadOnlyList<ConsentScope> Scopes =
    [
        new ConsentScope(CareRecipientId, DataCategory.Visits),
    ];

    private readonly INotificationService _service = Substitute.For<INotificationService>();
    private readonly IHealthDataAccessPolicy _accessPolicy =
        Substitute.For<IHealthDataAccessPolicy>();
    private readonly ICurrentNextOfKinAccessor _currentNextOfKin =
        Substitute.For<ICurrentNextOfKinAccessor>();
    private readonly NotificationsController _sut;

    public NotificationsControllerTests()
    {
        _accessPolicy
            .AuthorizeConsentedReadsAsync(Arg.Any<CancellationToken>())
            .Returns(new ConsentedAccess(NextOfKinId, Scopes));
        _accessPolicy
            .ResolveConsentedScopeAsync(Arg.Any<CancellationToken>())
            .Returns(new ConsentedAccess(NextOfKinId, Scopes));
        _currentNextOfKin
            .GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(new CurrentNextOfKin(NextOfKinId, [CareRecipientId]));
        _sut = new NotificationsController(_service, _accessPolicy, _currentNextOfKin);
    }

    private void GivenNobody()
    {
        _accessPolicy
            .AuthorizeConsentedReadsAsync(Arg.Any<CancellationToken>())
            .Returns((ConsentedAccess?)null);
        _accessPolicy
            .ResolveConsentedScopeAsync(Arg.Any<CancellationToken>())
            .Returns((ConsentedAccess?)null);
        _currentNextOfKin
            .GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns((CurrentNextOfKin?)null);
    }

    [Fact]
    public async Task Get_ReturnsTheInbox_ForTheScopeThePolicyGranted()
    {
        var inbox = new NotificationInboxResponse([], 3);
        _service.GetInboxAsync(NextOfKinId, Scopes, Arg.Any<CancellationToken>()).Returns(inbox);

        var result = await _sut.Get(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(inbox, okResult.Value);
    }

    // Passing anything but the policy's answer down would let the inbox show a
    // pair the policy never logged.
    [Fact]
    public async Task Get_HandsThePolicysScopeToTheService_Unchanged()
    {
        await _sut.Get(CancellationToken.None);

        await _service.Received(1).GetInboxAsync(NextOfKinId, Scopes, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WithoutQueryingTheService_WhenTheSessionResolvesToNobody()
    {
        GivenNobody();

        var result = await _sut.Get(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        await _service
            .DidNotReceive()
            .GetInboxAsync(
                Arg.Any<int>(),
                Arg.Any<IReadOnlyList<ConsentScope>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task MarkRead_ReturnsNoContent_WhenTheRowWasTheCallers()
    {
        _service.MarkReadAsync(NextOfKinId, Scopes, 9, Arg.Any<CancellationToken>()).Returns(true);

        Assert.IsType<NoContentResult>(await _sut.MarkRead(9, CancellationToken.None));
    }

    // Another person's id looks like a missing one, same as the visit endpoints.
    [Fact]
    public async Task MarkRead_ReturnsNotFound_WhenTheRowWasNotTheCallers()
    {
        _service.MarkReadAsync(NextOfKinId, Scopes, 9, Arg.Any<CancellationToken>()).Returns(false);

        Assert.IsType<NotFoundResult>(await _sut.MarkRead(9, CancellationToken.None));
    }

    [Fact]
    public async Task MarkRead_HandsThePolicysScopeToTheService_Unchanged()
    {
        await _sut.MarkRead(9, CancellationToken.None);

        await _service
            .Received(1)
            .MarkReadAsync(NextOfKinId, Scopes, 9, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkRead_ReturnsNotFound_WithoutCallingTheService_ForNobody()
    {
        GivenNobody();

        Assert.IsType<NotFoundResult>(await _sut.MarkRead(9, CancellationToken.None));
        await _service
            .DidNotReceive()
            .MarkReadAsync(
                Arg.Any<int>(),
                Arg.Any<IReadOnlyList<ConsentScope>>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task MarkAllRead_MarksTheCallersScopedRows_AndReturnsNoContent()
    {
        Assert.IsType<NoContentResult>(await _sut.MarkAllRead(CancellationToken.None));
        await _service
            .Received(1)
            .MarkAllReadAsync(NextOfKinId, Scopes, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkAllRead_ReturnsNotFound_ForNobody()
    {
        GivenNobody();

        Assert.IsType<NotFoundResult>(await _sut.MarkAllRead(CancellationToken.None));
        await _service
            .DidNotReceive()
            .MarkAllReadAsync(
                Arg.Any<int>(),
                Arg.Any<IReadOnlyList<ConsentScope>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GetPreferences_ReturnsTheCallersPreferences()
    {
        IReadOnlyList<NotificationPreferenceResponse> preferences =
        [
            new NotificationPreferenceResponse(ChangeKind.Completed, false),
        ];
        _service
            .GetPreferencesAsync(NextOfKinId, Arg.Any<CancellationToken>())
            .Returns(preferences);

        var result = await _sut.GetPreferences(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(preferences, okResult.Value);
    }

    [Fact]
    public async Task GetPreferences_ReturnsNotFound_ForNobody()
    {
        GivenNobody();

        var result = await _sut.GetPreferences(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetPreference_PassesTheChoiceDown_AndReturnsNoContent(bool enabled)
    {
        var result = await _sut.SetPreference(
            ChangeKind.Missed,
            new SetNotificationPreferenceRequest(enabled),
            CancellationToken.None
        );

        Assert.IsType<NoContentResult>(result);
        await _service
            .Received(1)
            .SetPreferenceAsync(
                NextOfKinId,
                ChangeKind.Missed,
                enabled,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SetPreference_ReturnsBadRequest_ForAKindThatDoesNotExist()
    {
        var result = await _sut.SetPreference(
            (ChangeKind)42,
            new SetNotificationPreferenceRequest(true),
            CancellationToken.None
        );

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.IsType<ValidationProblemDetails>(objectResult.Value);
        await _service
            .DidNotReceive()
            .SetPreferenceAsync(
                Arg.Any<int>(),
                Arg.Any<ChangeKind>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SetPreference_ReturnsNotFound_ForNobody()
    {
        GivenNobody();

        var result = await _sut.SetPreference(
            ChangeKind.Missed,
            new SetNotificationPreferenceRequest(true),
            CancellationToken.None
        );

        Assert.IsType<NotFoundResult>(result);
    }
}
