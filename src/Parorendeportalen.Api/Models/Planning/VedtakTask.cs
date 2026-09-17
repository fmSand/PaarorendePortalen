namespace Parorendeportalen.Api.Models.Planning;

// What the service does on a visit, as the vedtak lists it. Descriptive only: a
// source reports that a visit happened and says nothing about which tasks were done.
public class VedtakTask
{
    public int Id { get; set; }

    public int VedtakId { get; set; }

    public Vedtak Vedtak { get; set; } = null!;

    public string Description { get; set; } = string.Empty;

    // The municipality's own ordering, which is not alphabetical and carries meaning.
    public int Sequence { get; set; }
}
