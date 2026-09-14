namespace Parorendeportalen.Api.Models;

// The municipality's own catalogue is longer and differs per kommune. A source
// adapter maps its codes onto these; anything it can't map stays out rather than
// arriving as a value the day plan can't reason about.
public enum ServiceType
{
    // No zero value, so an unset field cannot pass for a real service.
    Hjemmesykepleie = 1,

    Hjemmehjelp = 2,

    Fysioterapi = 3,

    Ergoterapi = 4,

    Matombringing = 5,

    Dagsenter = 6,

    Trygghetsalarm = 7,
}
