namespace Parorendeportalen.Api.Models;

// What the service actually does on a visit, as the vedtak lists it. Descriptive,
// not a checklist: the portal is told that a visit happened, not what was done during it.
public class VedtakTask
{
    public int Id { get; set; }

    public int VedtakId { get; set; }

    public Vedtak Vedtak { get; set; } = null!;

    public string Description { get; set; } = string.Empty;

    // The municipality's own ordering, which is not alphabetical and carries meaning.
    public int Sequence { get; set; }
}
