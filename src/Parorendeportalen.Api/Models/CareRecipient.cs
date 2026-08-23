namespace Parorendeportalen.Api.Models;

public class CareRecipient
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public List<Visit> Visits { get; set; } = [];

    public List<NextOfKin> NextOfKin { get; set; } = [];
}
