using System.ComponentModel.DataAnnotations;

namespace Parorendeportalen.Api.Dtos;

// Nullable with Required, so a body that omits it is 400
public sealed record SetNotificationPreferenceRequest([property: Required] bool? Enabled);
