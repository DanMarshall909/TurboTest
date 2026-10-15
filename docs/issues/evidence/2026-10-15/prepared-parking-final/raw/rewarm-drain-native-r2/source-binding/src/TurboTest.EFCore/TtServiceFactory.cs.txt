using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using TurboTest.EFCore.Internal;

namespace TurboTest.EFCore;

/// <summary>Owns one configured fixture per context type for this service provider.</summary>
internal sealed class TtServiceFactory : ITtServiceFactory, IAsyncDisposable
{
    private sealed record Fixture(IAsyncDisposable Owner, Func<IReadOnlyList<string>> Resources);
    private readonly TtOptions _options;
    private readonly IServiceProvider _services;
    private readonly SemaphoreSlim _ownerGate = new(1, 1);
    private readonly object _gate = new();
    private readonly Dictionary<Type, Fixture> _owners = new();
    private readonly Dictionary<Guid, TtCleanup> _cleanup = new();
    private readonly Guid _cleanupOwner = Guid.NewGuid();
    private readonly CancellationTokenSource _shutdown = new();
    private bool _closing;
    private Task? _disposal;

    public TtServiceFactory(IOptions<TtOptions> options, IServiceProvider services)
    {
        _options = options.Value.Snapshot();
        _services = services;
    }

    public async Task<TtLease<TContext>> CreateLeaseAsync<TContext>(Action<DbContextOptionsBuilder>? configure = null)
        where TContext : DbContext
        => await (await CreateFactoryAsync<TContext>()).CreateLeaseAsync(configure);

    public async Task<TtFactory<TContext>> CreateFactoryAsync<TContext>() where TContext : DbContext
    {
        lock (_gate) ObjectDisposedException.ThrowIf(_closing, this);
        await _ownerGate.WaitAsync();
        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_closing, this);
                if (_owners.TryGetValue(typeof(TContext), out var existing))
                    return (TtFactory<TContext>)existing.Owner;
            }
            var baseline = _services.GetService<TtBaselineDefinition<TContext>>();
            TtFactory<TContext> owner;
            try
            {
                owner = baseline is null
                    ? await Tt.CreateOwnedFactoryAsync<TContext>(_options, _shutdown.Token)
                    : await Tt.CreatePreparedFactoryAsync(_options, baseline, _shutdown.Token);
            }
            catch (TtLifecycleException failure) when (failure.Cleanup is not null)
            {
                Retain(failure.Cleanup);
                throw;
            }
            lock (_gate)
            {
                _owners.Add(typeof(TContext), new Fixture(owner, () => owner.OwnedResourceIds));
                ObjectDisposedException.ThrowIf(_closing, this);
            }
            return owner;
        }
        finally { _ownerGate.Release(); }
    }

    public ValueTask DisposeAsync() => DisposeAsync(null);

    internal ValueTask DisposeAsync(Action? beforeKnownClose)
    {
        Task disposal;
        TaskCompletionSource? start = null;
        Fixture[] known = [];
        lock (_gate)
        {
            _closing = true;
            known = _owners.Values.ToArray();
            if (_disposal is null)
            {
                start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var drain = DisposeCoreAsync(start.Task);
                _disposal = ObserveDrainAsync(drain);
            }
            disposal = _disposal;
        }
        beforeKnownClose?.Invoke();
        // Every caller closes returned handles before returning, including a
        // peer that sees the published job before its first caller closes them.
        // Core drain joins the same cached jobs and in-flight initialization.
        foreach (var fixture in known) _ = fixture.Owner.DisposeAsync().AsTask();
        start?.TrySetResult();
        return new ValueTask(disposal);
    }

    private async Task DisposeCoreAsync(Task start)
    {
        await start;
        await _shutdown.CancelAsync();
        await _ownerGate.WaitAsync();
        Fixture[] fixtures;
        TtCleanup[] startup;
        try
        {
            fixtures = _owners.Values.ToArray();
            lock (_gate) startup = _cleanup.Values.ToArray();
        }
        finally { _ownerGate.Release(); }
        try
        {
            // Start every closure before awaiting one owner. The initialization
            // gate is free so already queued lookups can observe closure too.
            var owners = fixtures.Select(fixture => DrainOwnerAsync(fixture.Owner)).ToArray();
            await AwaitCleanupAsync(owners.Concat(startup.Select(cleanup => cleanup.RetryCleanupAsync())));
        }
        finally { _shutdown.Dispose(); }
    }

    private async Task DrainOwnerAsync(IAsyncDisposable owner)
    {
        try { await owner.DisposeAsync(); }
        catch (TtLifecycleException failure) when (failure.Cleanup is not null)
        {
            Retain(failure.Cleanup);
            throw;
        }
    }

    private void Retain(TtCleanup cleanup)
    {
        lock (_gate) _cleanup[cleanup.OwnerId] = cleanup;
    }

    private IReadOnlyList<string> OwnedResources()
    {
        lock (_gate)
            return _owners.Values.SelectMany(fixture => fixture.Resources())
                .Concat(_cleanup.Values.SelectMany(cleanup => cleanup.OwnedResourceIds)).Distinct().Order().ToArray();
    }

    private async Task ObserveDrainAsync(Task drain)
    {
        try { await drain.WaitAsync(_options.DrainTimeout); }
        catch (Exception error)
        {
            var failureReported = ReferenceEquals(drain.Exception?.InnerException, error);
            var cleanup = TtCleanupRegistry.Retain(_cleanupOwner, async () =>
            {
                Exception? delayedFailure = null;
                try { await drain; }
                catch (Exception failure) { delayedFailure = failure; }
                TtCleanup[] retained;
                lock (_gate) retained = _cleanup.Values.ToArray();
                await AwaitCleanupAsync(retained.Select(owner => owner.RetryCleanupAsync()));
                if (!failureReported && delayedFailure is not null &&
                    delayedFailure is not TtLifecycleException { Stage: "DrainTimeout" })
                {
                    failureReported = true;
                    throw delayedFailure as TtLifecycleException ?? new TtLifecycleException("ProviderDrain", delayedFailure);
                }
            }, OwnedResources);
            var stage = failureReported ? "ProviderDrain" : "DrainTimeout";
            var failure = error as TtLifecycleException ?? new TtLifecycleException(stage, error);
            throw failure.WithCleanup(cleanup);
        }
    }

    private static async Task AwaitCleanupAsync(IEnumerable<Task> jobs)
    {
        var all = Task.WhenAll(jobs);
        try { await all; }
        catch when (all.Exception is not null)
        {
            // Await propagates one failure; the completed task retains them all.
            // Deadlines describe observation, while material failures must remain
            // visible even when a different owner's deadline completed first.
            var failures = all.Exception.InnerExceptions
                .Where(error => error is not TtLifecycleException { Stage: "DrainTimeout" }).ToArray();
            if (failures.Length > 1)
                throw new TtLifecycleException("ProviderDrain", new AggregateException(failures));
            if (failures.Length == 1)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw;
        }
    }
}
