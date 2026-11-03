"""One-off TT-039 timing probe. Generate isolated source copies; never patch src/."""
import difflib
import hashlib
import json
from pathlib import Path
import sys

REPO = Path(__file__).resolve().parents[2]


def prepare(destination):
    destination = Path(destination)
    destination.mkdir(parents=True, exist_ok=False)
    changes = {}
    for source in sorted((REPO / "src/TurboTest.EFCore").rglob("*.cs")):
        relative = source.relative_to(REPO / "src/TurboTest.EFCore")
        if {"bin", "obj"} & set(relative.parts):
            continue
        original = source.read_text()
        text = original

        def replace(before, after):
            nonlocal text
            assert text.count(before) == 1, (relative, before, text.count(before))
            text = text.replace(before, after)

        if relative.name == "TtSlotPool.cs":
            replace('internal CancellationToken Token { get; } = token;',
                    'internal string? TraceRequest { get; } = TraceProbe.Request.Value;\n        internal CancellationToken Token { get; } = token;')
            replace('cancellationToken.ThrowIfCancellationRequested();\n        Reservation? preparation',
                    'cancellationToken.ThrowIfCancellationRequested();\n        TraceProbe.Emit("request");\n        Reservation? preparation')
            replace('_owned[ready] = State.Leased;', '_owned[ready] = State.Leased;\n                TraceProbe.Emit("admit", ready.Id);')
            replace('waiter.Node = _waiters.AddLast(waiter);', 'waiter.Node = _waiters.AddLast(waiter);\n            TraceProbe.Emit("queued");')
            replace('_owned[reservation] = State.WaitingReset;', '_owned[reservation] = State.WaitingReset;\n            TraceProbe.Emit("dirty", reservation.Id);')
            replace('bool backgroundReturn, bool stopOnFailure)\n    {\n        var permitHeld',
                    'bool backgroundReturn, bool stopOnFailure)\n    {\n        var traceReset = Guid.NewGuid();\n        TraceProbe.Emit("reset-request", reservation.Id, traceReset);\n        var permitHeld')
            replace('permitHeld = true;\n            lock (_gate) _owned[reservation] = State.ExecutingReset;',
                    'permitHeld = true;\n            TraceProbe.Emit("reset-start", reservation.Id, traceReset);\n            lock (_gate) _owned[reservation] = State.ExecutingReset;')
            replace('            PublishReady(reservation);\n        }\n        catch (OperationCanceledException error) when (backgroundReturn',
                    '            TraceProbe.Emit("service-end", reservation.Id, traceReset);\n            PublishReady(reservation, traceReset);\n        }\n        catch (OperationCanceledException error) when (backgroundReturn')
            replace('            if (permitHeld) _resetLimit.Release();\n            settled.TrySetResult();\n        }\n    }\n\n    private async Task<bool> RecreateAsync',
                    '            if (permitHeld)\n            {\n                TraceProbe.Emit("release-before", reservation.Id, traceReset);\n                _resetLimit.Release();\n                TraceProbe.Emit("release-after", reservation.Id, traceReset);\n            }\n            settled.TrySetResult();\n            TraceProbe.Emit("pool-settled", reservation.Id, traceReset);\n        }\n    }\n\n    private async Task<bool> RecreateAsync')
            replace('private void PublishReady(Reservation reservation)', 'private void PublishReady(Reservation reservation, Guid? traceReset = null)')
            replace('                _owned[reservation] = State.Leased;\n                reservation.IdleSince = null;',
                    '                _owned[reservation] = State.Leased;\n                TraceProbe.Emit("publish", reservation.Id, traceReset);\n                TraceProbe.Emit("admit", reservation.Id, request: waiter.TraceRequest);\n                reservation.IdleSince = null;')
            replace('            _owned[reservation] = State.Ready;\n            reservation.IdleSince',
                    '            _owned[reservation] = State.Ready;\n            TraceProbe.Emit("publish", reservation.Id, traceReset);\n            reservation.IdleSince')
        elif relative.name == "TtPreparedPool.cs":
            replace('var reservation = await _slots.AcquireAsync(cancellationToken);',
                    'var reservation = await _slots.AcquireAsync(cancellationToken);\n        TraceProbe.Emit("binding", reservation.Id, database: reservation.Value.Name, oid: reservation.Value.DatabaseOid);')
            replace('            _returns.Start(() => ReturnAsync(reservation));',
                    '            if (TraceProbe.AwaitReturn) return ReturnAsync(reservation);\n            _returns.Start(() => ReturnAsync(reservation));')
            replace('            catch (Exception) { /* Diagnostic consumers cannot replace the reset result. */ }',
                    '            catch (Exception) { /* Diagnostic consumers cannot replace the reset result. */ }\n            TraceProbe.Emit("complete", reservation.Id);')
        elif relative.name == "TtBackgroundReturns.cs":
            replace('                _pending.Remove(settled.Task);', '                _pending.Remove(settled.Task);\n                TraceProbe.Emit("background-complete");')
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text)
        changes[str(relative)] = dict(sourceSha256=hashlib.sha256(source.read_bytes()).hexdigest(),
                                     compiledSha256=hashlib.sha256(target.read_bytes()).hexdigest())
        if text != original:
            (destination / (relative.name + ".patch")).write_text(''.join(difflib.unified_diff(
                original.splitlines(True), text.splitlines(True), fromfile=str(source.relative_to(REPO)), tofile=str(relative))))
    (destination / "source-bindings.json").write_text(json.dumps(changes, indent=2) + "\n")


if __name__ == "__main__":
    prepare(sys.argv[1])
