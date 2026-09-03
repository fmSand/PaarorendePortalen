namespace Parorendeportalen.Api.Models;

// Returned by the access policy and stored on the log, so both carry one value.
public enum AccessDecision
{
    // No zero value: an unset outcome must not read as an authorised access.
    DeniedNoKinship = 1,
    DeniedNoConsent = 2,
    Granted = 3,
}
