using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Models.Access;
using Parorendeportalen.Api.Repositories.Access;
using Parorendeportalen.Api.Services.Kinship;

namespace Parorendeportalen.Api.Services.Access;

public sealed class HealthDataAccessPolicy(
    ICurrentNextOfKinAccessor currentNextOfKin,
    IConsentRepository consents,
    IAccessLogRepository accessLog,
    TimeProvider timeProvider
) : IHealthDataAccessPolicy
{
    public Task<AccessDecision> AuthorizeReadAsync(
        int careRecipientId,
        DataCategory category,
        CancellationToken cancellationToken
    ) => AuthorizeAsync(careRecipientId, category, AccessOperation.Read, cancellationToken);

    public Task<AccessDecision> AuthorizeWriteAsync(
        int careRecipientId,
        DataCategory category,
        CancellationToken cancellationToken
    ) => AuthorizeAsync(careRecipientId, category, AccessOperation.Write, cancellationToken);

    private async Task<AccessDecision> AuthorizeAsync(
        int careRecipientId,
        DataCategory category,
        AccessOperation operation,
        CancellationToken cancellationToken
    )
    {
        var current = await currentNextOfKin.GetCurrentAsync(cancellationToken);
        if (current is null)
        {
            // No next-of-kin to attribute a row to, so deny without writing one.
            return AccessDecision.DeniedNoKinship;
        }

        var now = timeProvider.GetUtcNow();
        var decision = await DecideAsync(
            current,
            careRecipientId,
            category,
            now,
            cancellationToken
        );

        // Written before the caller gets an answer, denials included, so an out-of-scope attempt is traceable.
        await accessLog.AppendAsync(
            Entry(current.NextOfKinId, careRecipientId, category, operation, decision, now),
            cancellationToken
        );

        return decision;
    }

    public async Task<ConsentedAccess?> AuthorizeConsentedReadsAsync(
        CancellationToken cancellationToken
    )
    {
        var now = timeProvider.GetUtcNow();
        var access = await ResolveAsync(now, cancellationToken);
        if (access is null)
        {
            return null;
        }

        foreach (var scope in access.Scopes)
        {
            await accessLog.AppendAsync(
                Entry(
                    access.NextOfKinId,
                    scope.CareRecipientId,
                    scope.Category,
                    AccessOperation.Read,
                    AccessDecision.Granted,
                    now
                ),
                cancellationToken
            );
        }

        return access;
    }

    public Task<ConsentedAccess?> ResolveConsentedScopeAsync(CancellationToken cancellationToken) =>
        ResolveAsync(timeProvider.GetUtcNow(), cancellationToken);

    private async Task<ConsentedAccess?> ResolveAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var current = await currentNextOfKin.GetCurrentAsync(cancellationToken);
        if (current is null)
        {
            return null;
        }

        var consented = await consents.GetActiveScopesAsync(
            current.NextOfKinId,
            now,
            cancellationToken
        );

        // Kinship first: a consent row for a care recipient the grant no longer covers opens nothing.
        var scopes = consented
            .Where(scope => current.CareRecipientIds.Contains(scope.CareRecipientId))
            .ToList();

        return new ConsentedAccess(current.NextOfKinId, scopes);
    }

    private async Task<AccessDecision> DecideAsync(
        CurrentNextOfKin current,
        int careRecipientId,
        DataCategory category,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        if (!current.CareRecipientIds.Contains(careRecipientId))
        {
            return AccessDecision.DeniedNoKinship;
        }

        var categories = await consents.GetActiveCategoriesAsync(
            current.NextOfKinId,
            careRecipientId,
            now,
            cancellationToken
        );

        return categories.Contains(category)
            ? AccessDecision.Granted
            : AccessDecision.DeniedNoConsent;
    }

    private static AccessLogEntry Entry(
        int nextOfKinId,
        int careRecipientId,
        DataCategory category,
        AccessOperation operation,
        AccessDecision outcome,
        DateTimeOffset occurredAt
    ) =>
        new()
        {
            OccurredAt = occurredAt,
            NextOfKinId = nextOfKinId,
            CareRecipientId = careRecipientId,
            Category = category,
            Operation = operation,
            Outcome = outcome,
        };
}
