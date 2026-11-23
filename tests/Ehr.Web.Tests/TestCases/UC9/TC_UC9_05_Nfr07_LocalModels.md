# TC-UC9-05 — Dictated words never leave the team's server or reach the logs

This case has two parts. The network part is an xUnit test beside this file (`TC_UC9_05_Nfr07_LocalModels.cs`), run by
`dotnet test`. The logging part is the dictation service's own pytest test, since the words are only ever in that
Python service: it is run as below, and by CI's dictation job on every change.

| Field | Value |
|---|---|
| Test Case ID | TC-UC9-05 |
| Purpose / scenario | Speech recognition and the language model run only on the team's server: their network has no route out, and a whole dictation, including a proposed order, writes none of its words to any log. |
| Type | Non-functional (privacy) |
| Requirements | NFR-07 |
| Use case | UC 9, Transcribe dictated audio, and UC 10, Structure and validate note fields; scope: the dictation service and its inference network |
| Preconditions | The repository checked out; uv and ffmpeg installed (`README.md`, "Dictation"). |
| Inputs and system conditions | `infra/compose/compose.yaml`; the dictation service's code with stand-ins for MedASR and the language model, so no model is needed. |
| Test data | A whole synthetic dictation: a recording made by the test's audio fixture, the stand-in MedASR hearing a fixed sentence, and a stand-in model proposing a history and the plan "Order zebrafinch panel." Every log record, standard output and standard error is captured. |
| Expected result | (1) `dotnet test` passes `TC_UC9_05_Nfr07_LocalModels`: the inference network is `internal: true`, and the dictation service and llama-server are on it alone and publish no port. (2) pytest passes `test_a_dictation_logs_none_of_its_words`: no word of what was said or proposed appears in any captured log line. |
| Creator | Samir Mukshe |

## Steps

1. Run the network part: `dotnet test --project tests/Ehr.Web.Tests --filter-class "Ehr.Web.Tests.TestCases.UC9.TC_UC9_05_Nfr07_LocalModels"`.
2. Run the logging part: `uv run --project src/Ehr.Dictation/sidecar --locked --python 3.12 pytest src/Ehr.Dictation/sidecar/tests/test_logging.py`.

## Results

| Part | Environment | Date run | Result |
|---|---|---|---|
| Network (xUnit) | Windows 11, .NET 10 SDK; main at `31af725` plus this change | 2026-11-22 | Pass |
| Logging (pytest) | Windows 11, Python 3.12.14 via uv, ffmpeg 9.0.2; main at `31af725` plus this change | 2026-11-22 | Pass: "1 passed" |
