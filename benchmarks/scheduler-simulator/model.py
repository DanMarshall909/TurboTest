"""Deterministic virtual warm test/reset pipeline. Durations are integer microseconds."""

from dataclasses import dataclass
from collections import deque
import heapq
import itertools


@dataclass(frozen=True)
class Work:
    body_us: int
    reset_us: int
    checkout_us: int = 0
    handoff_us: int = 0


def simulate(jobs, *, mode, slots, workers, reset_workers, release_tail_us=0):
    """Run a closed-loop workload to full settlement, independently of wall time.

    A slot becomes clean at publication; its reset permit can remain held longer.
    The caller supplies independent reset service draws, never trace/test matches.
    """
    jobs = tuple(jobs)
    if (mode not in ("awaited", "background") or not jobs
            or any(type(n) is not int or n <= 0 for n in (slots, workers, reset_workers))
            or type(release_tail_us) is not int or release_tail_us < 0
            or any(type(n) is not int or n < 0 for job in jobs
                   for n in (job.body_us, job.reset_us, job.checkout_us, job.handoff_us))):
        raise ValueError("ModelInput: require a mode, jobs, positive capacities and nonnegative integer timings")

    now = 0
    sequence = itertools.count()
    events = []
    clean = deque(range(slots))
    state = ["clean"] * slots
    checkouts, pending_resets = deque(), deque()
    tests, resets = {}, []
    active_resets = peak_resets = 0

    def schedule(delay, callback, *args):
        heapq.heappush(events, (now + delay, next(sequence), callback, args))

    def request(ordinal):
        if ordinal < len(jobs):
            tests[ordinal] = dict(ordinal=ordinal, worker=ordinal % workers, requested=now)
            checkouts.append(ordinal)
            admit_tests()

    def admit_tests():
        while clean and checkouts:
            ordinal, slot = checkouts.popleft(), clean.popleft()
            if state[slot] != "clean":
                raise RuntimeError("Conservation: dirty slot admission")
            state[slot] = "leased"
            tests[ordinal].update(slot=slot, queue_us=now - tests[ordinal]["requested"])
            schedule(jobs[ordinal].checkout_us, acquired, ordinal)

    def acquired(ordinal):
        tests[ordinal]["acquired"] = now
        schedule(jobs[ordinal].body_us, body_done, ordinal)

    def body_done(ordinal):
        tests[ordinal]["returning"] = now
        schedule(jobs[ordinal].handoff_us, handoff_done, ordinal)

    def returned(ordinal):
        tests[ordinal]["returned"] = now
        request(ordinal + workers)

    def handoff_done(ordinal):
        slot = tests[ordinal]["slot"]
        state[slot] = "queued"
        pending_resets.append(dict(ordinal=ordinal, slot=slot, queued=now))
        admit_resets()
        if mode == "background":
            returned(ordinal)

    def admit_resets():
        nonlocal active_resets, peak_resets
        while pending_resets and active_resets < reset_workers:
            reset = pending_resets.popleft()
            reset["started"] = now
            state[reset["slot"]] = "reset"
            active_resets += 1
            peak_resets = max(peak_resets, active_resets)
            resets.append(reset)
            schedule(jobs[reset["ordinal"]].reset_us, publish, reset)

    def publish(reset):
        reset["published"] = now
        state[reset["slot"]] = "clean"
        clean.append(reset["slot"])
        admit_tests()
        schedule(release_tail_us, release, reset)

    def release(reset):
        nonlocal active_resets
        reset["released"] = now
        active_resets -= 1
        admit_resets()
        if mode == "awaited":
            returned(reset["ordinal"])

    for worker in range(min(workers, len(jobs))):
        request(worker)
    while events:
        at, _, callback, args = heapq.heappop(events)
        if at < now:
            raise RuntimeError("Conservation: clock moved backwards")
        now = at
        callback(*args)
        # Slot ownership and permit ownership are separate conserved resources.
        if (len(clean) != state.count("clean") or len(set(clean)) != len(clean)
                or len(pending_resets) != state.count("queued")
                or not 0 <= state.count("reset") <= active_resets <= reset_workers):
            raise RuntimeError("Conservation: slot or reset permit accounting")
    if (checkouts or pending_resets or active_resets or len(clean) != slots
            or len(tests) != len(jobs) or len(resets) != len(jobs)
            or any("returned" not in row for row in tests.values())):
        raise RuntimeError("Conservation: abandoned work at final drain")
    last_return = max(row["returned"] for row in tests.values())
    return dict(suite_us=now, last_return_us=last_return, drain_us=now - last_return,
                tests=[tests[i] for i in range(len(jobs))], resets=resets,
                peak_resets=peak_resets, final_clean=len(clean), final_reset_permits=active_resets)
