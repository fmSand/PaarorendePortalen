using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;

namespace Parorendeportalen.Api.Services;

public sealed class HealthDataAccessPolicy(
    ICurrentNextOfKinAccessor currentNextOfKin,
    IConsentRepository consents,
    IAccessLogRepository accessLog,
    TimeProvider timeProvider
) : IHealthDataAccessPolicy
{
    public async Task<AccessDecision> AuthorizeReadAsync(
        int careRecipientId,
        DataCategory category,
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

        // Written before the caller gets an answer, denials included, so an
        // out-of-scope attempt is traceable too.
        await accessLog.AppendAsync(
            new AccessLogEntry
            {
                OccurredAt = now,
                NextOfKinId = current.NextOfKinId,
                CareRecipientId = careRecipientId,
                Category = category,
                Outcome = decision,
            },
            cancellationToken
        );

        return decision;
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
}
