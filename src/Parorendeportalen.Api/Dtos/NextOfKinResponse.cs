namespace Parorendeportalen.Api.Dtos;

public sealed record NextOfKinResponse(int Id, string DisplayName, IReadOnlyList<KinshipGrantResponse> Grants);

// Includes the name so the "choose a care recipient" screen only need one call
public sealed record KinshipGrantResponse(int CareRecipientId, string CareRecipientName, string? Relationship);
