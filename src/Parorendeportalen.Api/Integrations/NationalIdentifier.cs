namespace Parorendeportalen.Api.Integrations;

public readonly record struct NationalIdentifier
{
    public const string FodselsnummerSystem = "urn:oid:2.16.578.1.12.4.1.4.1";
    public const string DNummerSystem = "urn:oid:2.16.578.1.12.4.1.4.2";
    public const string FellesHjelpenummerSystem = "urn:oid:2.16.578.1.12.4.1.4.3";

    // Nationally allocated series only. A local hjelpenummer repeats across
    // institutions, so two people would resolve to one row.
    private static readonly string[] KnownSystems =
    [
        FodselsnummerSystem,
        DNummerSystem,
        FellesHjelpenummerSystem,
    ];

    public NationalIdentifier(string system, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(system);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!KnownSystems.Contains(system))
        {
            throw new ArgumentException(
                $"'{system}' is not a Norwegian national identifier system.",
                nameof(system)
            );
        }

        System = system;
        Value = value;
    }

    public string System { get; }

    public string Value { get; }

    // A struct keeps a parameterless constructor, so default() skips the guards.
    public bool IsSpecified => !string.IsNullOrWhiteSpace(System);

    // Changing this format orphans every hash already stored against it.
    public string HashInput =>
        IsSpecified
            ? $"{System}|{Value}"
            : throw new InvalidOperationException("An unspecified identifier has no hash input.");

    // A national id reaching a log line is what the hashing exists to prevent.
    public override string ToString() => $"{System}|<redacted>";
}
