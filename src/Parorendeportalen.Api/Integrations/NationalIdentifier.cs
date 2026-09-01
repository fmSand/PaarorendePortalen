namespace Parorendeportalen.Api.Integrations;

public readonly record struct NationalIdentifier
{
    public const string FodselsnummerSystem = "urn:oid:2.16.578.1.12.4.1.4.1";
    public const string DNummerSystem = "urn:oid:2.16.578.1.12.4.1.4.2";
    public const string HjelpenummerSystem = "urn:oid:2.16.578.1.12.4.1.4.3";

    private static readonly string[] KnownSystems =
        [FodselsnummerSystem, DNummerSystem, HjelpenummerSystem];

    public NationalIdentifier(string system, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(system);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!KnownSystems.Contains(system))
        {
            throw new ArgumentException(
                $"'{system}' is not a Norwegian national identifier system.",
                nameof(system));
        }

        System = system;
        Value = value;
    }

    public string System { get; }

    public string Value { get; }

    // A struct keeps a parameterless constructor, so default() skips the guards.
    public bool IsSpecified => !string.IsNullOrWhiteSpace(System);

    public override string ToString() => $"{System}|{Value}";
}
