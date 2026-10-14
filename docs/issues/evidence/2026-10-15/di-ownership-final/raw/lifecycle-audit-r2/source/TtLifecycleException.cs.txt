using Npgsql;

namespace TurboTest.EFCore;

/// <summary>A lifecycle failure with a safe stage and SQLSTATE diagnostic, without SQL or credentials.</summary>
public sealed class TtLifecycleException : InvalidOperationException
{
    public string Stage { get; }
    public string? SqlState { get; }
    public TtCleanup? Cleanup { get; }
    private readonly string _diagnostic;

    internal TtLifecycleException(string stage, Exception cause, Exception? cleanupFailure = null, TtCleanup? cleanup = null)
        : base(FormatMessage(stage, cause, cleanupFailure))
    {
        Stage = stage;
        SqlState = cause is TtLifecycleException lifecycle ? lifecycle.SqlState : (cause as PostgresException)?.SqlState;
        _diagnostic = Diagnostic(cause);
        Cleanup = cleanup ?? (cause as TtLifecycleException)?.Cleanup ?? (cleanupFailure as TtLifecycleException)?.Cleanup;
    }

    internal TtLifecycleException WithCleanupFailure(Exception error) => new(Stage, this, error);

    internal TtLifecycleException WithCleanup(TtCleanup cleanup) => new(this, cleanup);

    private TtLifecycleException(TtLifecycleException cause, TtCleanup cleanup) : base(cause.Message)
    {
        Stage = cause.Stage;
        SqlState = cause.SqlState;
        _diagnostic = cause._diagnostic;
        Cleanup = cleanup;
    }

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
