namespace Parorendeportalen.Api.Models;

// The municipality's own catalogue is longer and differs per kommune. A source adapter
// maps its codes onto these and leaves out what it can't map.
public enum ServiceType
{
    // No zero value, so an unset field can't pass for a service.
    Hjemmesykepleie = 1,

    Hjemmehjelp = 2,

    Fysioterapi = 3,

    Ergoterapi = 4,

    Matombringing = 5,

    Dagsenter = 6,

    Trygghetsalarm = 7,
}
