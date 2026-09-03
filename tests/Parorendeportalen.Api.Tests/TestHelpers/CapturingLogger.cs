using Microsoft.Extensions.Logging;

namespace Parorendeportalen.Api.Tests.TestHelpers;

/// <summary>
/// Keeps the warnings and errors a component logged, so a test that times out
/// waiting for work can say what went wrong instead of only that nothing
/// happened.
/// </summary>
internal class CapturingLogger : ILogger
{
    private readonly Lock _gate = new();
    private readonly List<string> _errors = [];
    private readonly List<string> _warnings = [];

    public IReadOnlyList<string> Errors => Copy(_errors);

    public IReadOnlyList<string> Warnings => Copy(_warnings);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var line = $"{formatter(state, exception)} {exception}".TrimEnd();

        lock (_gate)
        {
            (logLevel >= LogLevel.Error ? _errors : _warnings).Add(line);
        }
    }

    private IReadOnlyList<string> Copy(List<string> lines)
    {
        lock (_gate)
        {
            return [.. lines];
        }
    }
}

/// <summary>
/// The same, for a component that asks for its own category. A static class
/// cannot be a type argument, so the seeder takes the plain one above.
/// </summary>
internal sealed class CapturingLogger<T> : CapturingLogger, ILogger<T>;
