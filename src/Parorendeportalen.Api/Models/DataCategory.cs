namespace Parorendeportalen.Api.Models;

public enum DataCategory
{
    // No zero value: an unset category must not authorise a real one.
    Visits = 1,

    // Second category, so consent stays granular. Nothing serves it yet.
    Medications = 2,
}
