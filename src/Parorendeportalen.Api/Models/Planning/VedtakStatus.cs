namespace Parorendeportalen.Api.Models.Planning;

// The R4 request-status values a vedtak can be in. OnHold covers a pause, e.g. a
// hospital stay. Entered-in-error and unknown are left out, so next-of-kin never
// see a vedtak nobody stands behind.
public enum VedtakStatus
{
    // No zero value: an unset status must not read as one in force.
    Draft = 1,

    Active = 2,

    OnHold = 3,

    Revoked = 4,

    Completed = 5,
}
