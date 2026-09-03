namespace Parorendeportalen.Api.Tests.TestHelpers;

/// <summary>
/// A clock the test moves by hand, so behaviour that depends on time passing
/// can be asserted without waiting for it.
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
