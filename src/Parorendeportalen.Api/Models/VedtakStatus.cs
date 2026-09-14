namespace Parorendeportalen.Api.Models;

// The subset of R4 request-status a vedtak can actually be in. OnHold covers a
// pause, e.g. a hospital stay; entered-in-error and unknown are left out, since
// a vedtak nobody stands behind must not be shown to next-of-kin as if it applied.
public enum VedtakStatus
{
    // No zero value: an unset status must not read as one in force.
    Draft = 1,

    Active = 2,

    OnHold = 3,

    Revoked = 4,

    Completed = 5,
}
