using Microsoft.Extensions.Logging;

namespace KultureCompass.Infrastructure.Tests.Qloo;

/// <summary>Minimal ILogger<T> that records every formatted message, so tests can assert secrets never appear in
/// log output (02-security §7) without pulling in a logging test framework.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
    }
}
