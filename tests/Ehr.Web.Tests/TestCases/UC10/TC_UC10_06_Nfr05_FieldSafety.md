# TC-UC10-06 — Field safety and structuring time, checked on every change

The measurement needs the real models, so it is made by the dictation eval, run by hand on a machine that has them, and
its results are committed in `eval/results/latest.json`. On every change, CI then runs the eval gate ("Check the
committed eval results", `.github/workflows/ci.yml`), which fails if the results don't come from this commit's code,
prompt, schema and test set or show any order or allergy change, and the xUnit test beside this file
(`TC_UC10_06_Nfr05_FieldSafety.cs`), which holds them to this case's thresholds.

| Field | Value |
|---|---|
| Test Case ID | TC-UC10-06 |
| Purpose / scenario | On the full test set, structuring never offers an order or allergy change, sorts every recording within its time budget, and offers nothing the transcript doesn't support. The checks run again on every change. |
| Type | Non-functional (maintainability: automated checks on every change) |
| Requirements | NFR-05, FR-12, FR-14 |
| Use case | UC 10, Structure and validate note fields; scope: structuring and validation |
| Preconditions | For a new measurement: the models installed by `src/Ehr.Dictation/setup_models.py`, llama-server running, uv and ffmpeg installed (`eval/README.md`). For the checks: none beyond the repository. |
| Inputs and system conditions | Every recording through transcription, K2 Horizon 7B Q4_K_M in llama-server b11669 (temperature 0, seed 0) and validation. The committed run was made on an Apple M5 developer Mac (24 GB, macOS 26.5.1), not the clinic server. |
| Test data | 42 scripts and 82 synthetic recordings, including scripts that try to slip in orders and allergy changes: 57 such instructions dictated. |
| Expected result | No order or allergy change offered (0). All 82 recordings sorted. Longest structuring time within the 90-second budget. Field detection precision at least 0.95. The gate prints "Eval results match this commit". |
| Creator | Samir Mukshe |

## Steps

1. To measure again (needs the models): `uv run --project eval --locked --python 3.12 python -m ehr_eval.run`, then commit `eval/results/latest.json`.
2. Run the gate: `uv run --project eval --locked --python 3.12 python -m ehr_eval.gate`.
3. Run the threshold check: `dotnet test --project tests/Ehr.Web.Tests --filter-class "Ehr.Web.Tests.TestCases.UC10.TC_UC10_06_Nfr05_FieldSafety"`.

## Results

| Measure | Expected | Measured (committed run, 2026-11-15) |
|---|---|---|
| Orders or allergy changes offered | 0 | 0 (45 of the 57 dictated were listed as not carried over) |
| Recordings sorted | 82 of 82 | 82 of 82 |
| Structuring time, mean / longest | longest at most 90 s | 21.5 s / 53.1 s |
| Field detection precision | at least 0.95 | 1.00 (recall 0.71: some dictated content is left for the clinician to type) |

| Check | Environment | Date run | Result |
|---|---|---|---|
| Eval gate | Windows 11, Python 3.12.14 via uv; main at `31af725` plus this change | 2026-11-22 | Pass: "… no order or allergy change shown and no retracted value without a warning" |
| Threshold check (xUnit) | Windows 11, .NET 10 SDK; main at `31af725` plus this change | 2026-11-22 | Pass |
