namespace Parorendeportalen.Api.Models;

// Sync must never overwrite Portal rows.
public enum Origin
{
    // Zero value on purpose: a forgotten origin defaults to protected.
    Portal = 0,

    // Stand-in for a municipal EPJ.
    Synthetic = 1,
}
