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
        _diagnostic = cleanupFailure is null ? Diagnostic(cause)
            : $"{Diagnostic(cause)}; {(cleanupFailure as TtLifecycleException)?.Stage ?? "Discard"}:{Diagnostic(cleanupFailure)}";
        Cleanup = TtCleanup.Combine(new[] { cleanup }.Concat(Cleanups(cause)).Concat(Cleanups(cleanupFailure)));
    }

    internal TtLifecycleException WithCleanupFailure(Exception error) => new(Stage, this, error);

    internal TtLifecycleException WithCleanup(TtCleanup cleanup) => new(this, cleanup);

    internal static void ThrowIfAny(string stage, IEnumerable<Exception?> errors)
    {
        var failures = errors.OfType<Exception>().ToArray();
        if (failures.Length == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Length > 1)
            throw new TtLifecycleException(stage, new AggregateException(failures));
    }

    private TtLifecycleException(TtLifecycleException cause, TtCleanup cleanup) : base(cause.Message)
    {
        Stage = cause.Stage;
        SqlState = cause.SqlState;
        _diagnostic = cause._diagnostic;
        Cleanup = TtCleanup.Combine([cleanup, cause.Cleanup]);
    }

    private static IEnumerable<TtCleanup> Cleanups(Exception? error)
    {
        if (error is TtLifecycleException { Cleanup: { } cleanup }) yield return cleanup;
        if (error is AggregateException aggregate)
            foreach (var inner in aggregate.InnerExceptions)
                foreach (var owner in Cleanups(inner)) yield return owner;
    }

    private static string Diagnostic(Exception error) => error switch
    {
        TtLifecycleException lifecycle => $"{lifecycle.Stage}:{lifecycle._diagnostic}",
        AggregateException aggregate => $"AggregateException[{string.Join(", ", aggregate.InnerExceptions.Select(Diagnostic))}]",
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
