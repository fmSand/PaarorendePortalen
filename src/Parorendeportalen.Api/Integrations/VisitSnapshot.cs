using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Integrations;

// Carries provenance and non keys. Reconciling one into a Visit is the sync service's job
public sealed record VisitSnapshot
{
    private readonly SourceSystem _sourceSystem;
    private readonly string _externalId = string.Empty;
    private readonly NationalIdentifier _careRecipient;

    public required SourceSystem SourceSystem
    {
        get => _sourceSystem;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "A snapshot must name the system it came from."
                );
            }

            _sourceSystem = value;
        }
    }

    // Blank collapses the (SourceSystem, ExternalId) pair the upsert matches on.
    public required string ExternalId
    {
        get => _externalId;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _externalId = value;
        }
    }

    // `required` still allows default(), which carries no identifier.
    public required NationalIdentifier CareRecipient
    {
        get => _careRecipient;
        init
        {
            if (!value.IsSpecified)
            {
                throw new ArgumentException(
                    "A snapshot must identify its care recipient.",
                    nameof(value)
                );
            }

            _careRecipient = value;
        }
    }

    public required DateTimeOffset SourceUpdatedAt { get; init; }

    public required DateTimeOffset ScheduledAt { get; init; }

    public DateTimeOffset? ActualAt { get; init; }

    public required VisitStatus Status { get; init; }

    public string? CaregiverName { get; init; }

    public string? Notes { get; init; }
}
