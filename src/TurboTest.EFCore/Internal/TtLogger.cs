using Microsoft.Extensions.Logging;

namespace TurboTest.EFCore.Internal;

/// <summary>Applies the fixture's minimum level as well as the supplied logger's policy.</summary>
internal sealed class TtLogger(ILogger source, LogLevel minimum) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => source.BeginScope(state);
    public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum && source.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel)) source.Log(logLevel, eventId, state, exception, formatter);
    }
}
