using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Services;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Services;

public class HealthDataAccessPolicyTests
{
    private const int NextOfKinId = 5;
    private const int GrantedCareRecipientId = 7;
    private const int UngrantedCareRecipientId = 8;

    private readonly ICurrentNextOfKinAccessor _currentNextOfKin =
        Substitute.For<ICurrentNextOfKinAccessor>();
    private readonly IConsentRepository _consents = Substitute.For<IConsentRepository>();
    private readonly IAccessLogRepository _accessLog = Substitute.For<IAccessLogRepository>();
    private readonly FixedTimeProvider _clock = new(Snapshots.Noon);
    private readonly HealthDataAccessPolicy _sut;

    public HealthDataAccessPolicyTests()
    {
        _currentNextOfKin
            .GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(new CurrentNextOfKin(NextOfKinId, [GrantedCareRecipientId]));

        _sut = new HealthDataAccessPolicy(_currentNextOfKin, _consents, _accessLog, _clock);
    }

    private void GivenConsentFor(params DataCategory[] categories) =>
        _consents
            .GetActiveCategoriesAsync(
                NextOfKinId,
                GrantedCareRecipientId,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(categories);

    private Task<AccessDecision> Authorize(
        int careRecipientId = GrantedCareRecipientId,
        DataCategory category = DataCategory.Visits
    ) => _sut.AuthorizeReadAsync(careRecipientId, category, CancellationToken.None);

    [Fact]
    public async Task Grants_WhenCallerHoldsAGrantAndAConsentForTheCategory()
    {
        GivenConsentFor(DataCategory.Visits);

        Assert.Equal(AccessDecision.Granted, await Authorize());
    }

    [Fact]
    public async Task DeniesForNoKinship_WithoutConsultingConsent_WhenCallerHoldsNoGrant()
    {
        GivenConsentFor(DataCategory.Visits);

        var decision = await Authorize(careRecipientId: UngrantedCareRecipientId);

        Assert.Equal(AccessDecision.DeniedNoKinship, decision);
        await _consents
            .DidNotReceive()
            .GetActiveCategoriesAsync(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task DeniesForNoConsent_WhenCallerHoldsAGrantButNoConsentAtAll()
    {
        GivenConsentFor();

        Assert.Equal(AccessDecision.DeniedNoConsent, await Authorize());
    }

    // Consent is per category. Holding one does not open the others.
    [Fact]
    public async Task DeniesForNoConsent_WhenTheConsentCoversAnotherCategory()
    {
        GivenConsentFor(DataCategory.Medications);

        Assert.Equal(
            AccessDecision.DeniedNoConsent,
            await Authorize(category: DataCategory.Visits)
        );
    }

    [Fact]
    public async Task ConsultsConsent_ForTheCallerAndTheRequestedCareRecipient_AtTheClocksNow()
    {
        GivenConsentFor(DataCategory.Visits);

        await Authorize();

        await _consents
            .Received(1)
            .GetActiveCategoriesAsync(
                NextOfKinId,
                GrantedCareRecipientId,
                Snapshots.Noon,
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(GrantedCareRecipientId, DataCategory.Visits, AccessDecision.Granted)]
    [InlineData(GrantedCareRecipientId, DataCategory.Medications, AccessDecision.DeniedNoConsent)]
    [InlineData(UngrantedCareRecipientId, DataCategory.Visits, AccessDecision.DeniedNoKinship)]
    public async Task LogsEveryOutcome_WithWhoAboutWhomWhatWhenAndTheDecision(
        int careRecipientId,
        DataCategory category,
        AccessDecision expected
    )
    {
        GivenConsentFor(DataCategory.Visits);

        var decision = await Authorize(careRecipientId, category);

        Assert.Equal(expected, decision);
        await _accessLog
            .Received(1)
            .AppendAsync(
                Arg.Is<AccessLogEntry>(entry =>
                    entry.NextOfKinId == NextOfKinId
                    && entry.CareRecipientId == careRecipientId
                    && entry.Category == category
                    && entry.Outcome == expected
                    && entry.OccurredAt == Snapshots.Noon
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task DeniesForNoKinship_AndLogsNothing_WhenTheSessionResolvesToNobody()
    {
        _currentNextOfKin
            .GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns((CurrentNextOfKin?)null);

        var decision = await Authorize();

        Assert.Equal(AccessDecision.DeniedNoKinship, decision);
        await _accessLog
            .DidNotReceive()
            .AppendAsync(Arg.Any<AccessLogEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Throws_WhenTheLogCannotBeWritten()
    {
        GivenConsentFor(DataCategory.Visits);
        _accessLog
            .AppendAsync(Arg.Any<AccessLogEntry>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("log store down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Authorize());
    }

    private void GivenScopes(params ConsentScope[] scopes) =>
        _consents
            .GetActiveScopesAsync(
                NextOfKinId,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(scopes);

    private Task<ConsentedAccess?> AuthorizeConsented() =>
        _sut.AuthorizeConsentedReadsAsync(CancellationToken.None);

    private Task AssertLoggedGranted(int careRecipientId, DataCategory category) =>
        _accessLog
            .Received(1)
            .AppendAsync(
                Arg.Is<AccessLogEntry>(entry =>
                    entry.NextOfKinId == NextOfKinId
                    && entry.CareRecipientId == careRecipientId
                    && entry.Category == category
                    && entry.Outcome == AccessDecision.Granted
                    && entry.OccurredAt == Snapshots.Noon
                ),
                Arg.Any<CancellationToken>()
            );

    [Fact]
    public async Task ConsentedReads_ReturnEveryConsentedPair_AndLogEachAsGranted()
    {
        GivenScopes(
            new ConsentScope(GrantedCareRecipientId, DataCategory.Visits),
            new ConsentScope(GrantedCareRecipientId, DataCategory.Medications)
        );

        var access = await AuthorizeConsented();

        Assert.NotNull(access);
        Assert.Equal(NextOfKinId, access.NextOfKinId);
        Assert.Equal(
            [
                new ConsentScope(GrantedCareRecipientId, DataCategory.Visits),
                new ConsentScope(GrantedCareRecipientId, DataCategory.Medications),
            ],
            access.Scopes
        );
        await AssertLoggedGranted(GrantedCareRecipientId, DataCategory.Visits);
        await AssertLoggedGranted(GrantedCareRecipientId, DataCategory.Medications);
    }

    [Fact]
    public async Task ConsentedReads_DropAConsentOutsideTheGrant_AndLogNothingForIt()
    {
        GivenScopes(
            new ConsentScope(GrantedCareRecipientId, DataCategory.Visits),
            new ConsentScope(UngrantedCareRecipientId, DataCategory.Visits)
        );

        var access = await AuthorizeConsented();

        Assert.Equal(
            [new ConsentScope(GrantedCareRecipientId, DataCategory.Visits)],
            access!.Scopes
        );
        await _accessLog
            .Received(1)
            .AppendAsync(Arg.Any<AccessLogEntry>(), Arg.Any<CancellationToken>());
        await _accessLog
            .DidNotReceive()
            .AppendAsync(
                Arg.Is<AccessLogEntry>(entry => entry.CareRecipientId == UngrantedCareRecipientId),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ConsentedReads_ReturnNoScopes_AndLogNothing_WhenNothingIsConsented()
    {
        GivenScopes();

        var access = await AuthorizeConsented();

        Assert.NotNull(access);
        Assert.Empty(access.Scopes);
        await _accessLog
            .DidNotReceive()
            .AppendAsync(Arg.Any<AccessLogEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConsentedReads_ReturnNull_AndLogNothing_WhenTheSessionResolvesToNobody()
    {
        _currentNextOfKin
            .GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns((CurrentNextOfKin?)null);

        Assert.Null(await AuthorizeConsented());
        await _accessLog
            .DidNotReceive()
            .AppendAsync(Arg.Any<AccessLogEntry>(), Arg.Any<CancellationToken>());
        await _consents
            .DidNotReceive()
            .GetActiveScopesAsync(
                Arg.Any<int>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ConsentedReads_AskForTheCallersScopes_AtTheClocksNow()
    {
        GivenScopes();

        await AuthorizeConsented();

        await _consents
            .Received(1)
            .GetActiveScopesAsync(NextOfKinId, Snapshots.Noon, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConsentedReads_Throw_WhenTheLogCannotBeWritten()
    {
        GivenScopes(new ConsentScope(GrantedCareRecipientId, DataCategory.Visits));
        _accessLog
            .AppendAsync(Arg.Any<AccessLogEntry>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("log store down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => AuthorizeConsented());
    }
}
