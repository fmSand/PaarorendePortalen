using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Models.Visits;

namespace Parorendeportalen.Api.Integrations;

public sealed record VisitSnapshot
{
    private readonly SourceSystem _sourceSystem;
    private readonly string _externalId = string.Empty;
    private readonly NationalIdentifier _careRecipient;
    private readonly DateTimeOffset _sourceUpdatedAt;
    private readonly DateTimeOffset _scheduledAt;
    private readonly DateTimeOffset? _actualAt;

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

    // A source is free to send Oslo local time. Everything downstream, the stored visit
    // and the watermark, is an instant, so the three timestamps convert on arrival.
    public required DateTimeOffset SourceUpdatedAt
    {
        get => _sourceUpdatedAt;
        init => _sourceUpdatedAt = value.ToUniversalTime();
    }

    public required DateTimeOffset ScheduledAt
    {
        get => _scheduledAt;
        init => _scheduledAt = value.ToUniversalTime();
    }

    public DateTimeOffset? ActualAt
    {
        get => _actualAt;
        init => _actualAt = value?.ToUniversalTime();
    }

    public required VisitStatus Status { get; init; }

    public ServiceType? ServiceType { get; init; }

    public string? CaregiverName { get; init; }

    public string? Notes { get; init; }
}
