using Microsoft.Extensions.Logging;

namespace TurboTest.EFCore.Internal;

/// <summary>Reserves workload capacity before backend work and atomically assigns verified results.</summary>
internal sealed class TtSlotPool<T> : IAsyncDisposable where T : class
{
    private const int RecoveryAttempts = 3;
    private static readonly TimeSpan RecoveryDelay = TimeSpan.FromMilliseconds(100);
    private enum State { Preparing, Ready, Leased, WaitingReset, ExecutingReset, Quarantined, Parking, Parked, Rewarming, Retiring }

    internal sealed class Reservation(Guid id, bool recovering = false, int preparationAttempts = 1)
    {
        internal Guid Id { get; } = id;
        internal bool Recovering { get; } = recovering;
        internal int PreparationAttempts { get; } = preparationAttempts;
        internal T? Resource { get; set; }
        // Replacement/retry retains its promise even while deletion owns the resource.
        internal bool WarmCommitted { get; set; } = true;
        internal long? IdleSince { get; set; }
        internal TtLifecycleException? UnseenPreparationFailure { get; set; }
        internal T Value => Resource ?? throw new InvalidOperationException("The reservation has no prepared resource.");
        internal TaskCompletionSource Settled { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Waiter(CancellationToken token, long requested)
    {
        internal CancellationToken Token { get; } = token;
        internal long Requested { get; } = requested;
        internal long CompletedAt { get; set; }
        internal string Outcome { get; set; } = "Succeeded";
        internal TaskCompletionSource<Reservation> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal LinkedListNode<Waiter>? Node { get; set; }
        internal CancellationTokenRegistration Cancellation { get; set; }
    }

    private readonly object _gate = new();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly int _capacity;
    private readonly Func<Guid, T> _reserve;
    private readonly Func<T, CancellationToken, Task> _prepare;
    private readonly Func<T, Task> _delete;
    private readonly Func<T, CancellationToken, Task>? _rewarm;
    private readonly TimeProvider _clock;
    private readonly TtTelemetry _telemetry;
    private readonly TtCapacityPolicy? _capacityPolicy;
    private readonly Dictionary<Reservation, State> _owned = new();
    private readonly Queue<Reservation> _ready = new();
    private readonly LinkedList<Waiter> _waiters = new();
    private readonly List<TtLifecycleException> _lateFailures = [];
    private readonly SemaphoreSlim _preparationLimit;
    private readonly SemaphoreSlim _resetLimit;
    private readonly int _resetConcurrency;
    private readonly CancellationTokenSource _closing = new();
    private bool _closed;
    private bool _stopping;
    private TtLifecycleException? _failure;
    private Task? _disposal;

    internal TtSlotPool(int capacity, int preparationConcurrency,
        Func<Guid, T> reserve, Func<T, CancellationToken, Task> prepare, Func<T, Task> delete,
        int resetConcurrency = 1, Func<T, CancellationToken, Task>? rewarm = null, TimeProvider? timeProvider = null,
        ILogger? logger = null, LogLevel logLevel = LogLevel.Debug, TtCapacityPolicy? capacityPolicy = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(preparationConcurrency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resetConcurrency);
        _capacity = capacity;
        _reserve = reserve;
        _prepare = prepare;
        _delete = delete;
        _rewarm = rewarm;
        _clock = timeProvider ?? TimeProvider.System;
        _capacityPolicy = capacityPolicy;
        _telemetry = new TtTelemetry(logger, logLevel, () => Snapshot, _clock);
        _preparationLimit = new SemaphoreSlim(preparationConcurrency, preparationConcurrency);
        _resetLimit = new SemaphoreSlim(resetConcurrency, resetConcurrency);
        _resetConcurrency = resetConcurrency;
    }

    internal TtPoolSnapshot Snapshot
    {
        get
        {
            lock (_gate)
                return new TtPoolSnapshot(_owner, _capacity, Count(State.Preparing), Count(State.Ready),
                    Count(State.Leased), Count(State.WaitingReset), Count(State.ExecutingReset),
                    Count(State.Quarantined), Count(State.Parked), Count(State.Retiring), _waiters.Count)
                { Parking = Count(State.Parking), Rewarming = Count(State.Rewarming),
                    ExecutingCleanup = _resetConcurrency - _resetLimit.CurrentCount,
                    WarmTarget = _capacityPolicy?.Target ?? _capacity };
        }
    }

    private int Count(State state) => _owned.Values.Count(value => value == state);

    internal async Task<TtCapacityDecision> TickCapacityAsync(TtMemoryReading? memory,
        Func<T, CancellationToken, Task>? park = null, int warmFloor = 0, int? maxParked = null,
        TimeSpan? idleLifetime = null)
    {
        var started = _clock.GetTimestamp();
        Reservation? rewarming = null;
        Reservation? preparation = null;
        List<(Reservation Reservation, TaskCompletionSource Settled, bool Retire)> idle = [];
        TtCapacityDecision decision;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed || _stopping, this);
            if (_capacityPolicy is null) return new(_capacity, _capacity, "Fixed");
            decision = _failure is null ? _capacityPolicy.Evaluate(Snapshot, memory)
                : new(_capacityPolicy.Target, _capacityPolicy.Target, "Unavailable");
            if (decision.Target > decision.PreviousTarget)
            {
                rewarming = ClaimParked(replenish: true);
                if (rewarming is null) preparation = ReservePending(replenish: true);
            }
            else if (decision.Target < decision.PreviousTarget && park is not null)
            {
                var surplus = WarmCount() - decision.Target;
                idle = ClaimIdle(Math.Max(warmFloor, _ready.Count - surplus), maxParked ?? _capacity, idleLifetime);
            }
        }
        var outcome = "Succeeded";
        try
        {
            if (rewarming is not null) await RewarmAsync(rewarming);
            if (preparation is not null) await PrepareAsync(preparation);
            if (park is not null) await RunIdleClaimsAsync(idle, park);
            lock (_gate) if (_failure is not null) outcome = "Failed";
            return decision;
        }
        catch (Exception error) { outcome = TtTelemetry.Outcome(error); throw; }
        finally { _telemetry.RecordCapacity(decision, memory, _clock.GetElapsedTime(started), outcome); }
    }

    // Caller holds the ledger lock; promised preparation/rewarm is warm capacity.
    private int WarmCount() => _owned.Keys.Count(reservation => reservation.WarmCommitted);
    private bool CanWarm() => _capacityPolicy is null || WarmCount() < _capacityPolicy.Target;

    private void ObserveDemand() => _capacityPolicy?.ObserveDemand(Snapshot);

    internal Task ParkIdleAsync(int warmFloor, Func<T, CancellationToken, Task> park, int? maxParked = null,
        TimeSpan? idleLifetime = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(warmFloor);
        if (warmFloor > _capacity) throw new ArgumentOutOfRangeException(nameof(warmFloor));
        var retainedLimit = maxParked ?? _capacity;
        if (retainedLimit < 0 || retainedLimit > _capacity) throw new ArgumentOutOfRangeException(nameof(maxParked));
        if (idleLifetime is { } lifetime && lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleLifetime));
        List<(Reservation Reservation, TaskCompletionSource Settled, bool Retire)> selected;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed || _stopping, this);
            if (_failure is not null) throw _failure;
            if (_capacityPolicy is not null)
                warmFloor = Math.Max(warmFloor, _capacityPolicy.Target - (WarmCount() - _ready.Count));
            selected = ClaimIdle(warmFloor, retainedLimit, idleLifetime);
        }
        return RunIdleClaimsAsync(selected, park);
    }

    private Task RunIdleClaimsAsync(List<(Reservation Reservation, TaskCompletionSource Settled, bool Retire)> selected,
        Func<T, CancellationToken, Task> park) => Task.WhenAll(selected.Select(item => item.Retire
            ? RetireIdleAsync(item.Reservation, item.Settled)
            : ParkAsync(item.Reservation, item.Settled, park)));

    // Caller holds the ledger lock, including when a policy decision selects Ready capacity.
    private List<(Reservation Reservation, TaskCompletionSource Settled, bool Retire)> ClaimIdle(int warmFloor,
        int retainedLimit, TimeSpan? idleLifetime)
    {
        List<(Reservation Reservation, TaskCompletionSource Settled, bool Retire)> selected = [];
        var now = _clock.GetTimestamp();
        var retained = Count(State.Parked) + Count(State.Parking);
        foreach (var reservation in _owned.Where(item => item.Value == State.Parked).Select(item => item.Key).ToArray())
        {
            if (retained <= retainedLimit && !Expired(reservation)) continue;
            Claim(reservation, retire: true);
            retained--;
        }
        while (_ready.Count > warmFloor)
        {
            var reservation = _ready.Dequeue();
            var retire = Expired(reservation) || retained >= retainedLimit;
            Claim(reservation, retire);
            if (!retire) retained++;
        }
        ObserveDemand();
        return selected;

        bool Expired(Reservation reservation) => idleLifetime is { } duration &&
            reservation.IdleSince is { } since && _clock.GetElapsedTime(since, now) >= duration;

        void Claim(Reservation reservation, bool retire)
        {
            reservation.WarmCommitted = false;
            _owned[reservation] = retire ? State.Retiring : State.Parking;
            var settled = reservation.Settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            selected.Add((reservation, settled, retire));
        }
    }

    private async Task RetireIdleAsync(Reservation reservation, TaskCompletionSource settled)
    {
        var permitHeld = false;
        try
        {
            await _resetLimit.WaitAsync(_closing.Token);
            permitHeld = true;
            await _delete(reservation.Value);
            ReplenishAfterDeletion(reservation, replenish: false);
        }
        catch (Exception error)
        {
            var failure = error as TtLifecycleException ?? new TtLifecycleException("SlotIdleRetire", error);
            ScheduleCleanupRecovery(reservation, failure, replenish: false);
            throw failure;
        }
        finally
        {
            if (permitHeld) _resetLimit.Release();
            settled.TrySetResult();
        }
    }

    private async Task ParkAsync(Reservation reservation, TaskCompletionSource settled, Func<T, CancellationToken, Task> park)
    {
        var permitHeld = false;
        try
        {
            await _resetLimit.WaitAsync(_closing.Token);
            permitHeld = true;
            await park(reservation.Value, _closing.Token);
            Reservation? rewarming;
            lock (_gate)
            {
                _owned[reservation] = _closed ? State.Retiring : State.Parked;
                rewarming = ClaimParked();
            }
            if (rewarming is not null) _ = RewarmAsync(rewarming);
        }
        catch (Exception error)
        {
            if (!permitHeld)
            {
                await _resetLimit.WaitAsync();
                permitHeld = true;
            }
            throw await RecoverFailedOperationAsync(reservation, error, "SlotPark");
        }
        finally
        {
            if (permitHeld) _resetLimit.Release();
            settled.TrySetResult();
        }
    }

    internal Task<Reservation> AcquireAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reservation? preparation = null;
        Reservation? rewarming = null;
        Waiter waiter;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed || _stopping, this);
            if (_failure is not null)
            {
                MarkFailureDelivered(_failure);
                throw _failure;
            }
            if (_ready.TryDequeue(out var ready))
            {
                _owned[ready] = State.Leased;
                ready.IdleSince = null;
                ObserveDemand();
                return Task.FromResult(ready);
            }
            waiter = new Waiter(cancellationToken, _clock.GetTimestamp());
            waiter.Node = _waiters.AddLast(waiter);
            waiter.Cancellation = cancellationToken.Register(() => Cancel(waiter));
            ObserveDemand();
            rewarming = ClaimParked();
            if (rewarming is null) preparation = ReservePending();
        }
        if (rewarming is not null) _ = RewarmAsync(rewarming);
        if (preparation is not null) _ = PrepareAsync(preparation);
        return ObserveQueueAsync(waiter);
    }

    private async Task<Reservation> ObserveQueueAsync(Waiter waiter)
    {
        try { return await waiter.Completion.Task; }
        finally
        {
            _telemetry.Record("QueueWait", _clock.GetElapsedTime(waiter.Requested, waiter.CompletedAt), waiter.Outcome);
        }
    }

    // Caller holds the ledger lock. Reuse is claimed before considering fresh capacity.
    private Reservation? ClaimParked(bool replenish = false)
    {
        if (_rewarm is null || _closed || _stopping || _failure is not null || (!replenish && _waiters.Count == 0) || !CanWarm()) return null;
        var reservation = _owned.FirstOrDefault(item => item.Value == State.Parked).Key;
        if (reservation is null) return null;
        reservation.WarmCommitted = true;
        _owned[reservation] = State.Rewarming;
        reservation.Settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return reservation;
    }

    private async Task RewarmAsync(Reservation reservation)
    {
        var settled = reservation.Settled;
        var permitHeld = false;
        try
        {
            await _telemetry.MeasureAsync("RewarmPermitWait", () => _preparationLimit.WaitAsync(_closing.Token));
            permitHeld = true;
            await _telemetry.MeasureAsync("RewarmExecution", () => _rewarm!(reservation.Value, _closing.Token));
            PublishReady(reservation);
        }
        catch (Exception error)
        {
            var failure = error as TtLifecycleException ?? new TtLifecycleException("SlotRewarm", error);
            if (error is not OperationCanceledException)
                lock (_gate) reservation.UnseenPreparationFailure = failure;
            // Replacement preparation may already hold the cleanup permit.
            // Release preparation before joining that permit to avoid inversion.
            if (permitHeld) { _preparationLimit.Release(); permitHeld = false; }
            await _resetLimit.WaitAsync();
            try
            {
                await RecoverFailedOperationAsync(reservation, failure, "SlotRewarm");
            }
            finally { _resetLimit.Release(); }
        }
        finally
        {
            if (permitHeld) _preparationLimit.Release();
            settled.TrySetResult();
        }
    }

    // Caller holds the ledger lock. The placeholder exists before backend work.
    private Reservation? ReservePending(bool replenish = false, int preparationAttempts = RecoveryAttempts)
    {
        if (_closed || _stopping || _failure is not null || (!replenish && _waiters.Count == 0) || _owned.Count >= _capacity || !CanWarm()) return null;
        var reservation = new Reservation(Guid.NewGuid(), replenish, replenish ? preparationAttempts : 1);
        _owned.Add(reservation, State.Preparing);
        return reservation;
    }

    private void Cancel(Waiter waiter)
    {
        lock (_gate)
        {
            if (waiter.Node is null) return;
            _waiters.Remove(waiter.Node);
            waiter.Node = null;
            waiter.CompletedAt = _clock.GetTimestamp();
            waiter.Outcome = "Canceled";
            waiter.Completion.TrySetCanceled(waiter.Token);
            ObserveDemand();
        }
    }

    private async Task PrepareAsync(Reservation reservation)
    {
        var settled = reservation.Settled;
        var attempts = reservation.PreparationAttempts;
        try
        {
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    _closing.Token.ThrowIfCancellationRequested();
                    lock (_gate)
                    {
                        if (_stopping)
                        {
                            _owned[reservation] = State.Retiring;
                            return;
                        }
                        _owned[reservation] = State.Preparing;
                        reservation.Resource = _reserve(reservation.Id);
                    }
                    await PrepareResourceAsync(reservation);
                    PublishReady(reservation);
                    return;
                }
                catch (Exception error)
                {
                    var failure = error as TtLifecycleException ?? new TtLifecycleException("SlotPrepare", error);
                    // Preparation owns material failures until verified publication
                    // or delivery, including abandoned initial acquisitions.
                    if (error is not OperationCanceledException)
                        lock (_gate)
                        {
                            if (reservation.UnseenPreparationFailure is { } previous)
                                failure = new TtLifecycleException("SlotPrepare", new AggregateException(previous, failure));
                            reservation.UnseenPreparationFailure = failure;
                        }
                    await _resetLimit.WaitAsync();
                    try
                    {
                        lock (_gate) _owned[reservation] = State.Retiring;
                        if (reservation.Resource is not null) await _delete(reservation.Value);
                        reservation.Resource = null;
                        lock (_gate)
                        {
                            if (attempt == attempts || _closed || _stopping)
                            {
                                FailAfterConfirmedDeletion(reservation, failure);
                                return;
                            }
                            _owned[reservation] = State.Quarantined;
                        }
                    }
                    catch (Exception cleanupError)
                    {
                        lock (_gate)
                        {
                            failure = (reservation.UnseenPreparationFailure ?? failure).WithCleanupFailure(cleanupError);
                            reservation.UnseenPreparationFailure = failure;
                        }
                        if (reservation.Recovering)
                            ScheduleCleanupRecovery(reservation, failure, attempts - attempt);
                        else
                            Quarantine(reservation, failure, "SlotPrepare");
                        return;
                    }
                    finally { _resetLimit.Release(); }
                    await Task.Delay(RecoveryDelay, _clock, _closing.Token);
                }
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            lock (_gate) _owned[reservation] = State.Retiring;
        }
        finally { settled.TrySetResult(); }
    }

    private async Task PrepareResourceAsync(Reservation reservation, Func<T, CancellationToken, Task>? prepareReplacement = null)
    {
        await _telemetry.MeasureAsync("PreparationPermitWait", () => _preparationLimit.WaitAsync(_closing.Token));
        try { await _telemetry.MeasureAsync("Preparation", () => (prepareReplacement ?? _prepare)(reservation.Value, _closing.Token)); }
        finally { _preparationLimit.Release(); }
    }

    private void FailWaiters(Exception error)
    {
        while (_waiters.First is { } node)
        {
            _waiters.Remove(node);
            node.Value.Node = null;
            node.Value.Cancellation.Unregister();
            node.Value.CompletedAt = _clock.GetTimestamp();
            node.Value.Outcome = TtTelemetry.Outcome(error);
            if (node.Value.Completion.TrySetException(error) && error is TtLifecycleException failure)
                MarkFailureDelivered(failure);
        }
        ObserveDemand();
    }

    // Caller holds the ledger lock. A fault handed to an acquisition is no
    // longer an unseen diagnostic obligation for final drain.
    private void MarkFailureDelivered(TtLifecycleException failure)
    {
        foreach (var reservation in _owned.Keys)
            if (ReferenceEquals(reservation.UnseenPreparationFailure, failure)) reservation.UnseenPreparationFailure = null;
        _lateFailures.RemoveAll(recorded => ReferenceEquals(recorded, failure));
    }

    private void RetainUnseenFailure(Reservation reservation)
    {
        if (reservation.UnseenPreparationFailure is not { } failure) return;
        _lateFailures.Add(failure);
        reservation.UnseenPreparationFailure = null;
    }

    internal Task ReturnAsync(Reservation reservation, Func<T, CancellationToken, Task> reset)
        => ReturnAsync(reservation, async (resource, token) => { await reset(resource, token); return true; });

    internal Task ReturnAsync(Reservation reservation, Func<T, CancellationToken, Task<bool>> reset,
        Action<TimeSpan>? observeWait = null, Func<T, CancellationToken, Task>? prepareReplacement = null)
        => ResetAsync(reservation, reset, BeginReturn(reservation), observeWait, prepareReplacement);

    internal async Task WarmAsync(int count, CancellationToken cancellationToken)
    {
        var reservations = await Task.WhenAll(Enumerable.Range(0, count).Select(_ => AcquireAsync(cancellationToken)));
        await Task.WhenAll(reservations.Select(reservation => ReturnAsync(reservation, (_, _) => Task.CompletedTask)));
    }

    internal Task DiscardAsync(Reservation reservation)
        => DiscardCoreAsync(reservation, BeginReturn(reservation));

    private TaskCompletionSource BeginReturn(Reservation reservation)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (!_owned.TryGetValue(reservation, out var state) || state != State.Leased)
                throw new InvalidOperationException("The reservation is not leased by this pool.");
            _owned[reservation] = State.WaitingReset;
            return reservation.Settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private async Task DiscardCoreAsync(Reservation reservation, TaskCompletionSource settled)
    {
        try
        {
            await _resetLimit.WaitAsync(_closing.Token);
            try
            {
                lock (_gate) _owned[reservation] = State.Retiring;
                await _delete(reservation.Value);
                ReplenishAfterDeletion(reservation, replenish: false);
            }
            finally { _resetLimit.Release(); }
        }
        catch (Exception error)
        {
            throw Quarantine(reservation, error, "SlotDiscard");
        }
        finally { settled.TrySetResult(); }
    }

    private async Task ResetAsync(Reservation reservation, Func<T, CancellationToken, Task<bool>> reset,
        TaskCompletionSource settled, Action<TimeSpan>? observeWait, Func<T, CancellationToken, Task>? prepareReplacement)
    {
        var permitHeld = false;
        try
        {
            lock (_gate)
            {
                if (_stopping)
                {
                    _owned[reservation] = State.Retiring;
                    return;
                }
            }
            var requested = _clock.GetTimestamp();
            try { await _telemetry.MeasureAsync("ResetPermitWait", () => _resetLimit.WaitAsync(_closing.Token)); }
            finally { observeWait?.Invoke(_clock.GetElapsedTime(requested)); }
            permitHeld = true;
            lock (_gate) _owned[reservation] = State.ExecutingReset;
            if (!await _telemetry.MeasureAsync("ResetExecution", () => reset(reservation.Value, _closing.Token)))
            {
                if (!await _telemetry.MeasureAsync("Recreation", () => RecreateAsync(reservation, prepareReplacement))) return;
            }
            PublishReady(reservation);
        }
        catch (Exception error)
        {
            if (!permitHeld)
            {
                await _resetLimit.WaitAsync();
                permitHeld = true;
            }
            throw await RecoverFailedOperationAsync(reservation, error, "SlotReset");
        }
        finally
        {
            if (permitHeld) _resetLimit.Release();
            settled.TrySetResult();
        }
    }

    private async Task<bool> RecreateAsync(Reservation reservation, Func<T, CancellationToken, Task>? prepareReplacement)
    {
        lock (_gate) _owned[reservation] = State.Retiring;
        await _delete(reservation.Value);
        lock (_gate)
        {
            if (_closed || _stopping)
            {
                _owned.Remove(reservation);
                return false;
            }
            reservation.Resource = _reserve(reservation.Id);
            _owned[reservation] = State.Preparing;
        }
        await PrepareResourceAsync(reservation, prepareReplacement);
        return true;
    }

    // Caller owns the cleanup permit. Every operation uses the same bounded
    // recovery episode and releases capacity only after confirmed deletion.
    private async Task<TtLifecycleException> RecoverFailedOperationAsync(Reservation reservation, Exception error, string stage)
    {
        var failure = error as TtLifecycleException ?? new TtLifecycleException(stage, error);
        try
        {
            lock (_gate) _owned[reservation] = State.Retiring;
            await _telemetry.MeasureAsync("RecoveryDeletion", () => _delete(reservation.Value));
            ReplenishAfterDeletion(reservation, replenish: true);
        }
        catch (Exception cleanupError)
        {
            failure = failure.WithCleanupFailure(cleanupError);
            lock (_gate)
                if (reservation.UnseenPreparationFailure is not null) reservation.UnseenPreparationFailure = failure;
            ScheduleCleanupRecovery(reservation, failure);
        }
        return failure;
    }

    // The caller holds the ledger lock and has confirmed that no resource remains.
    private void FailAfterConfirmedDeletion(Reservation reservation, TtLifecycleException failure)
    {
        _owned.Remove(reservation);
        RetainUnseenFailure(reservation);
        _failure ??= failure;
        FailWaiters(_failure);
    }

    private void ReplenishAfterDeletion(Reservation reservation, bool replenish, int preparationAttempts = RecoveryAttempts)
    {
        Reservation? replacement;
        lock (_gate)
        {
            _owned.Remove(reservation);
            replacement = ReservePending(replenish, preparationAttempts);
            if (replacement is not null)
            {
                replacement.UnseenPreparationFailure = reservation.UnseenPreparationFailure;
                reservation.UnseenPreparationFailure = null;
            }
            else RetainUnseenFailure(reservation);
        }
        if (replacement is not null) _ = PrepareAsync(replacement);
    }

    private void ScheduleCleanupRecovery(Reservation reservation, TtLifecycleException failure,
        int preparationAttempts = RecoveryAttempts, bool replenish = true)
    {
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _owned[reservation] = State.Quarantined;
            if (_closed || _stopping) return;
            reservation.Settled = settled;
        }
        _ = RecoverCleanupAsync(reservation, failure, settled, preparationAttempts, replenish);
    }

    private async Task RecoverCleanupAsync(Reservation reservation, TtLifecycleException failure, TaskCompletionSource settled,
        int preparationAttempts, bool replenish)
    {
        try
        {
            for (var attempt = 2; attempt <= RecoveryAttempts; attempt++)
            {
                await Task.Delay(RecoveryDelay, _clock, _closing.Token);
                lock (_gate) if (_stopping) return;
                await _resetLimit.WaitAsync(_closing.Token);
                try
                {
                    lock (_gate) _owned[reservation] = State.Retiring;
                    await _delete(reservation.Value);
                    if (preparationAttempts == 0)
                    {
                        lock (_gate)
                        {
                            FailAfterConfirmedDeletion(reservation, failure);
                        }
                    }
                    else ReplenishAfterDeletion(reservation, replenish, preparationAttempts);
                    return;
                }
                catch (Exception error)
                {
                    failure = failure.WithCleanupFailure(error);
                    lock (_gate)
                        if (reservation.UnseenPreparationFailure is not null) reservation.UnseenPreparationFailure = failure;
                    if (attempt == RecoveryAttempts)
                    {
                        Quarantine(reservation, failure, "SlotRecovery");
                        return;
                    }
                    lock (_gate) _owned[reservation] = State.Quarantined;
                }
                finally { _resetLimit.Release(); }
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            lock (_gate) _owned[reservation] = State.Quarantined;
        }
        finally { settled.TrySetResult(); }
    }

    private TtLifecycleException Quarantine(Reservation reservation, Exception error, string stage)
    {
        var failure = error as TtLifecycleException ?? new TtLifecycleException(stage, error);
        lock (_gate)
        {
            _owned[reservation] = State.Quarantined;
            _failure ??= failure;
            FailWaiters(_failure);
        }
        return failure;
    }

    private void PublishReady(Reservation reservation)
    {
        lock (_gate)
        {
            if (_closed || _stopping)
            {
                _owned[reservation] = State.Retiring;
                return;
            }
            // Only a fully verified publication completes transparent recovery.
            reservation.UnseenPreparationFailure = null;
            while (_waiters.First is { } node)
            {
                var waiter = node.Value;
                _waiters.Remove(node);
                waiter.Node = null;
                waiter.Cancellation.Unregister();
                waiter.CompletedAt = _clock.GetTimestamp();
                if (waiter.Token.IsCancellationRequested)
                {
                    waiter.Outcome = "Canceled";
                    waiter.Completion.TrySetCanceled(waiter.Token);
                    continue;
                }
                _owned[reservation] = State.Leased;
                reservation.IdleSince = null;
                waiter.Completion.TrySetResult(reservation);
                ObserveDemand();
                return;
            }
            _owned[reservation] = State.Ready;
            reservation.IdleSince = _clock.GetTimestamp();
            _ready.Enqueue(reservation);
            ObserveDemand();
        }
    }

    public ValueTask DisposeAsync()
    {
        Task disposal;
        var cancel = false;
        lock (_gate)
        {
            if (_disposal is null)
            {
                _closed = true;
                FailWaiters(new ObjectDisposedException(nameof(TtSlotPool<T>)));
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposal = DisposeCoreAsync(start.Task);
                disposal = _disposal;
                cancel = true;
                start.TrySetResult();
            }
            else disposal = _disposal;
        }
        if (cancel) _closing.Cancel();
        return new ValueTask(disposal);
    }

    internal void StopAcceptingWork()
    {
        lock (_gate)
        {
            _stopping = true;
            FailWaiters(new ObjectDisposedException(nameof(TtSlotPool<T>)));
        }
    }

    internal Task RetryCleanupAsync()
    {
        lock (_gate)
        {
            if (_disposal is not null)
            {
                if (_disposal.IsFaulted)
                {
                    var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _disposal = DisposeCoreAsync(start.Task);
                    start.TrySetResult();
                }
                return _disposal;
            }
        }
        return DisposeAsync().AsTask();
    }

    private async Task DisposeCoreAsync(Task start)
    {
        await start;
        Reservation[] reservations;
        lock (_gate) reservations = _owned.Keys.ToArray();
        await Task.WhenAll(reservations.Select(reservation => reservation.Settled.Task));
        // A recorded discard can finish while drain waits for its settlement.
        lock (_gate) reservations = _owned.Keys.ToArray();
        Exception? first;
        lock (_gate)
        {
            foreach (var reservation in reservations) RetainUnseenFailure(reservation);
            first = _lateFailures.Count switch
            {
                0 => null,
                1 => _lateFailures[0],
                _ => new AggregateException(_lateFailures)
            };
            _lateFailures.Clear();
        }
        foreach (var reservation in reservations)
        {
            lock (_gate) _owned[reservation] = State.Retiring;
            try
            {
                if (reservation.Resource is not null) await _delete(reservation.Value);
                lock (_gate) _owned.Remove(reservation);
            }
            catch (Exception error)
            {
                lock (_gate) _owned[reservation] = State.Quarantined;
                first = first is null ? error : new AggregateException(first, error);
            }
        }
        lock (_gate) _ready.Clear();
        if (first is not null) throw new TtLifecycleException("SlotDispose", first);
    }
}
