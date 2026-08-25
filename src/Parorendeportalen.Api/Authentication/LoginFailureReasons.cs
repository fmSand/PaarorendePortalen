namespace Parorendeportalen.Api.Authentication;

public static class LoginFailureReasons
{
    public const string NoSubClaim = "Idura did not return a 'sub' claim.";
    public const string NoSocialNoClaim = "Idura did not return a 'socialno' claim — SSN scope/consent may not be configured.";
    public const string NoGrant = "No registered next-of-kin relation for this identity.";

    public static readonly string[] All = [NoSubClaim, NoSocialNoClaim, NoGrant];
}
