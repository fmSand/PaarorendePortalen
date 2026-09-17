namespace Parorendeportalen.Api.Models.Visits;

// Who, among the next-of-kin around one care recipient, an entry is for.
public enum Visibility
{
    // an entry whose visibility was never set stays with its author.
    Private = 0,

    // Every next-of-kin the care recipient has shared this category with.
    Shared = 1,
}
