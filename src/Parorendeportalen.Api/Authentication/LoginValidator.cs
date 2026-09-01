using System.Security.Claims;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Authentication;

public sealed record LoginResult(string? FailureReason, int? NextOfKinId)
{
    public static LoginResult Accepted(int nextOfKinId) => new(null, nextOfKinId);

    public static LoginResult Rejected(string failureReason) => new(failureReason, null);

    public bool IsRejected => FailureReason is not null;
}

public sealed class LoginValidator(
    INextOfKinService nextOfKinService,
    ILogger<LoginValidator> logger
)
{
    public async Task<LoginResult> ValidateAsync(
        ClaimsPrincipal? principal,
        CancellationToken cancellationToken
    )
    {
        var externalId = principal?.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(externalId))
        {
            logger.LogWarning("OIDC token validated with no 'sub' claim — rejecting login.");
            return LoginResult.Rejected(LoginFailureReasons.NoSubClaim);
        }

        var nationalId = principal?.FindFirst("socialno")?.Value;
        if (string.IsNullOrEmpty(nationalId))
        {
            logger.LogWarning("OIDC token validated with no 'socialno' claim — rejecting login.");
            return LoginResult.Rejected(LoginFailureReasons.NoSocialNoClaim);
        }

        var displayName = principal?.FindFirst("name")?.Value ?? externalId;

        var nextOfKin = await nextOfKinService.ResolveOrBindAsync(
            externalId,
            nationalId,
            displayName,
            cancellationToken
        );

        if (nextOfKin is null)
        {
            logger.LogWarning("OIDC login with no registered kinship grant — rejecting login.");
            return LoginResult.Rejected(LoginFailureReasons.NoGrant);
        }

        logger.LogInformation("NextOfKin {NextOfKinId} authenticated via OIDC.", nextOfKin.Id);
        return LoginResult.Accepted(nextOfKin.Id);
    }
}
