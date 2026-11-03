# Scheduler reference model

This offline TT-039 reference models the existing awaited/background warm FIFO
pipeline. **It fails its frozen prediction gate and is not qualified for policy
searches.** The [protocol](protocol.md) and [input pins](inputs.json) were committed
at `6f7298d` before fitting. See the [results](../../docs/issues/evidence/2026-11-03/tt039/report.md).

Python 3.10+ and its standard library are sufficient. Docker, .NET and package
installation are unnecessary. From the repository root, using the retained local
TT-038 working traces:

```sh
python3 benchmarks/scheduler-simulator/reference.py fit --trace-root artifacts/tt038/v4 --output artifacts/tt039-rerun/fit.json
python3 benchmarks/scheduler-simulator/reference.py validate --trace-root artifacts/tt038/v4 --fit artifacts/tt039-rerun/fit.json --output artifacts/tt039-rerun/evaluation.json
```

`fit` reads service samples from calibration runs only and saves the empirical
distributions. `validate` checks the saved fit against those inputs before
predicting. Exit 0 means a qualified gate, **2 means completed evaluation with a
failed gate**, and 1 means an input/output error. An argument error also uses
argparse's exit 2 but produces no evaluation report. Existing output files are
preserved. Timings vary; fixed seeds, service samples and predictions reproduce.

For a fresh checkout, the slow contract below reads only the 84 pinned files from
the [published TT-038 archive](../../docs/issues/evidence/2026-11-02/tt038/raw-evidence.tar.gz)
into temporary storage, verifies them and exercises both CLI commands. Its test
passes when the recorded **negative** model result reproduces. That test result
does not qualify the simulator. Temporary extraction and outputs are removed
when the contract finishes, including assertion failures.

The canonical test runner is the repository's existing Python `unittest` runner:

| Lane | Command | Purpose |
| --- | --- | --- |
| fast | `python3 -m unittest discover -s benchmarks/scheduler-simulator -p 'test_*.py'` | Deterministic pipeline, fitting, partition, metrics and failure controls |
| slow | `python3 -m unittest discover -s benchmarks/scheduler-simulator -p archive_contract.py` | Actual archive/CLI reproduction without Docker |
| all | Run both commands above | Exact union of 15 fast tests and one slow test |

Both lanes run in CI. The fast lane takes about 0.2 s locally; the archive lane
about 10 s. The expensive work belongs to the archive contract; fast tests retain
the queueing, gate and sampling rules with real model code and small workloads.
Standard-library `trace --count --missing` can inspect execution coverage; it is
not assertion coverage or branch coverage.

The kernel accepts synthetic capacities for deterministic safety controls. The
reference evaluator fixes capacity to the recorded contract and exposes no
policy sweep. Empirical costs are mode-conditioned, CPU/I/O demand is unknown,
and reset-to-test correlation is unobserved. The protocol specifies all modelling
approximations and the evidence needed before any new ranking claim.
