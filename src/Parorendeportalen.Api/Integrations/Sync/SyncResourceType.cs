namespace Parorendeportalen.Api.Integrations.Sync;

// The other half of the watermark key. Vedtak and dagsplan come from the same
// systems as visits and must not share a watermark with them.
public enum SyncResourceType
{
    // No zero value, so an unset resource type cannot key a watermark.
    Visit = 1,
}
