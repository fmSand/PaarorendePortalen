namespace Parorendeportalen.Api.Models;

public enum DataCategory
{
    // No zero value, so an unset category matches no consent.
    Visits = 1,

    // Second category, so consent stays granular. Nothing serves it yet.
    Medications = 2,

    Vedtak = 3,
}
