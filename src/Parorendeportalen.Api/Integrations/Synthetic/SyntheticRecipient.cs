namespace Parorendeportalen.Api.Integrations.Synthetic;

// Key goes into the ExternalId the upsert writes against, so it has to stay
// with the person when the seed list gains or loses an entry.
public sealed record SyntheticRecipient(string Key, NationalIdentifier Identifier);
