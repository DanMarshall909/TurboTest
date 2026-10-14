using Npgsql;

namespace TurboTest.EFCore;

/// <summary>A lifecycle failure with a safe stage and SQLSTATE diagnostic, without SQL or credentials.</summary>
public sealed class TtLifecycleException : InvalidOperationException
{
    public string Stage { get; }
    public string? SqlState { get; }
    private readonly string _diagnostic;

    internal TtLifecycleException(string stage, Exception cause, Exception? cleanupFailure = null)
        : base(FormatMessage(stage, cause, cleanupFailure))
    {
        Stage = stage;
        SqlState = cause is TtLifecycleException lifecycle ? lifecycle.SqlState : (cause as PostgresException)?.SqlState;
        _diagnostic = Diagnostic(cause);
    }

    internal TtLifecycleException WithCleanupFailure(Exception error) => new(Stage, this, error);

    private static string Diagnostic(Exception error) => error switch
    {
        TtLifecycleException lifecycle => lifecycle._diagnostic,
        PostgresException postgres => postgres.SqlState,
        _ => error.GetType().Name
    };

    private static string FormatMessage(string stage, Exception cause, Exception? cleanupFailure)
    {
        var message = $"TurboTest lifecycle failed at {stage} ({Diagnostic(cause)}). The resource is unavailable for reuse.";
        if (cleanupFailure is null) return message;
        var cleanupStage = cleanupFailure is TtLifecycleException lifecycle ? lifecycle.Stage : "Discard";
        return $"{message} Cleanup also failed at {cleanupStage} ({Diagnostic(cleanupFailure)}).";
    }
}
