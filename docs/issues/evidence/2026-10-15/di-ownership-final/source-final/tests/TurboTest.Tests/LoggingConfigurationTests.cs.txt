using Microsoft.Extensions.Logging;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class LoggingConfigurationTests
{
    [Theory]
    [InlineData(LogLevel.Warning, true)]
    [InlineData(LogLevel.Information, true)]
    [InlineData(LogLevel.Debug, true)]
    [InlineData(LogLevel.Debug, false)]
    public async Task Configured_minimum_level_and_supplied_logger_policy_filter_owner_logging(LogLevel minimum, bool enabled)
    {
        var logger = new RecordingLogger(enabled);
        await using var pool = new TtPool(new TtOptions
        {
            AutoAllocateResources = true, MaxAutoContainers = 1,
            Logger = logger, LogLevel = minimum
        });
        Assert.All(logger.Levels, level => Assert.True(level >= minimum));
        if (!enabled || minimum == LogLevel.Warning) Assert.Empty(logger.Levels);
        else Assert.Contains(LogLevel.Information, logger.Levels);
        if (enabled && minimum == LogLevel.Debug) Assert.Contains(LogLevel.Debug, logger.Levels);
    }

    private sealed class RecordingLogger(bool enabled) : ILogger
    {
        internal List<LogLevel> Levels { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => enabled;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }
}
