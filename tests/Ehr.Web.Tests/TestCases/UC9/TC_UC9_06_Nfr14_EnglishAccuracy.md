# TC-UC9-06 — English dictation accuracy and live-transcript speed

The measurement needs the real models (MedASR and K2 Horizon 7B), so it is made by the dictation eval, run by hand on a
machine that has them, and its results are committed in `eval/results/latest.json`. Two automated checks then run on
every change: CI's eval gate, which fails if the results don't come from this commit's code, prompt, schema and test
set, and the xUnit test beside this file (`TC_UC9_06_Nfr14_EnglishAccuracy.cs`), which holds them to this case's
thresholds.

| Field | Value |
|---|---|
| Test Case ID | TC-UC9-06 |
| Purpose / scenario | English dictation is transcribed accurately across accents and background noise, and quickly enough for a live transcript. |
| Type | Non-functional (constraint: English dictation; performance of the live transcript) |
| Requirements | NFR-14, FR-07 |
| Use case | UC 9, Transcribe dictated audio; scope: speech recognition (MedASR) and the live transcript |
| Preconditions | For a new measurement: the models installed by `src/Ehr.Dictation/setup_models.py`, llama-server running, uv and ffmpeg installed (`eval/README.md`). For the checks: none beyond the repository. |
| Inputs and system conditions | The eval test set through the dictation service's own decoding, MedASR and checks, each recording whole and as one-second chunks. The committed run was made on an Apple M5 developer Mac (24 GB, macOS 26.5.1), not the clinic server, with llama-server b11669, temperature 0, seed 0. |
| Test data | 42 scripts and 82 synthetic English recordings (US, British, Spanish, French and Hindi accents; babble, HVAC and monitor noise at 0 to 20 dB); 1,277 one-second chunks timed. |
| Expected result | The results cover all 82 recordings. Overall word error rate at most 10%. A one-second chunk is answered within 1 s at the 95th percentile. The gate prints "Eval results match this commit". |
| Creator | Samir Mukshe |

## Steps

1. To measure again (needs the models): `uv run --project eval --locked --python 3.12 python -m ehr_eval.run`, then commit `eval/results/latest.json`.
2. Run the gate: `uv run --project eval --locked --python 3.12 python -m ehr_eval.gate`.
3. Run the threshold check: `dotnet test --project tests/Ehr.Web.Tests --filter-class "Ehr.Web.Tests.TestCases.UC9.TC_UC9_06_Nfr14_EnglishAccuracy"`.

## Results

| Measure | Expected | Measured (committed run, 2026-11-15) |
|---|---|---|
| Recordings covered | 82 | 82, complete |
| Word error rate, overall | at most 10% | 8.3% (0.0834) |
| Word error rate, babble at 5 dB / 0 dB | not a criterion | 33% / 95%: in loud crowd noise a headset is needed |
| Live chunk answer time, p50 / p95 / max | p95 at most 1 s | 0.149 s / 0.229 s / 0.294 s |

| Check | Environment | Date run | Result |
|---|---|---|---|
| Eval gate | Windows 11, Python 3.12.14 via uv; main at `31af725` plus this change | 2026-11-22 | Pass: "Eval results match this commit (no accepted baseline yet): WER 0.0834 … every recording sorted" |
| Threshold check (xUnit) | Windows 11, .NET 10 SDK; main at `31af725` plus this change | 2026-11-22 | Pass |

The gate has no accepted baseline yet: a baseline is recorded on the clinic server, and until then "no lower than the
baseline" has nothing to compare with. The measurement was made on a developer Mac, not the server.
