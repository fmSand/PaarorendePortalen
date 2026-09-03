namespace Parorendeportalen.Api.Integrations.Sync;

public sealed class VisitSyncOptions
{
    public const string SectionName = "VisitSync";

    // Shifting more than this cannot change the capped multiplier, and keeps
    // the shift itself inside an int. 1 << 31 is negative.
    private const int MaxExponent = 30;

    // A source down this long should still be tried daily.
    private static readonly TimeSpan MaxDelay = TimeSpan.FromDays(1);

    // Task.Delay refuses anything longer, and the worker awaits it outside the
    // try that keeps a failing tick off the host.
    public static readonly TimeSpan MaxPollInterval = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    public bool Enabled { get; init; } = true;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMinutes(15);

    public int MaxBackoffMultiplier { get; init; } = 8;

    // The poll interval is the retry. The watermark only moves on a run that
    // finished, so the next tick refetches exactly what this one failed on.
    public TimeSpan DelayAfter(int consecutiveFailures)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(consecutiveFailures);

        if (consecutiveFailures == 0)
        {
            return PollInterval;
        }

        var multiplier = Math.Min(
            1 << Math.Min(consecutiveFailures, MaxExponent),
            MaxBackoffMultiplier
        );

        // In double, so the product cannot overflow before the ceiling clamps
        // it. Never shorter than the interval that was configured.
        var ceiling = Math.Max(PollInterval.Ticks, MaxDelay.Ticks);
        var ticks = Math.Min((double)PollInterval.Ticks * multiplier, ceiling);

        return TimeSpan.FromTicks((long)ticks);
    }
}
