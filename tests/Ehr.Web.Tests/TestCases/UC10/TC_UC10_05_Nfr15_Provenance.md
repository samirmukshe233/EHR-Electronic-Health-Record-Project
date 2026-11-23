# TC-UC10-05 — Every result names the models that produced it

This case has two parts. The web app's part is an xUnit test beside this file (`TC_UC10_05_Nfr15_Provenance.cs`), run
by `dotnet test`: the result names its models and code, and that is kept with the review. The dictation service's part
is its own pytest tests, since only the service reads the model files: it records each model's repository, commit and
hash, and refuses to start with weights it can't account for. Both run in CI on every change.

| Field | Value |
|---|---|
| Test Case ID | TC-UC10-05 |
| Purpose / scenario | Every result records exactly which licensed models, prompt and code produced it, and the service refuses to run with model weights it can't account for, so no result names a model it didn't use. |
| Type | Non-functional (legal: license terms) |
| Requirements | NFR-15 |
| Use case | UC 10, Structure and validate note fields; scope: dictation service provenance |
| Preconditions | The repository checked out; uv and ffmpeg installed (`README.md`, "Dictation"). |
| Inputs and system conditions | A temporary models folder with a `models-manifest.json`; the dictation service with stand-ins for MedASR and the language model. |
| Test data | A manifest recording google/medasr and IFM/K2-Horizon-7B-GGUF (`K2-Horizon-7B-Q4_K_M.gguf`), each with a commit and SHA-256; MedASR weights whose hash the manifest doesn't record; no manifest at all. |
| Expected result | (1) The manifest's repository, commit and hash are read for each model. (2) The service refuses to start with unrecorded MedASR weights, and (3) with no manifest. (4) A whole dictation's result names the models, the llama-server build, the prompt, the schema and the code. (5) `dotnet test` passes `TC_UC10_05_Nfr15_Provenance`. |
| Creator | Samir Mukshe |

## Steps

1. Run the service's tests: `uv run --project src/Ehr.Dictation/sidecar --locked --python 3.12 pytest src/Ehr.Dictation/sidecar/tests/test_provenance.py "src/Ehr.Dictation/sidecar/tests/test_sessions.py::test_every_result_says_which_models_prompt_and_schema_made_it"`.
2. Run the web app's part: `dotnet test --project tests/Ehr.Web.Tests --filter-class "Ehr.Web.Tests.TestCases.UC10.TC_UC10_05_Nfr15_Provenance"`.

## Results

| Part | Environment | Date run | Result |
|---|---|---|---|
| Service (pytest): `test_provenance.py` (3 tests) and `test_every_result_says_which_models_prompt_and_schema_made_it` | Windows 11, Python 3.12.14 via uv, ffmpeg 9.0.2; main at `31af725` plus this change | 2026-11-22 | Pass: "4 passed" |
| Web app (xUnit) | Windows 11, .NET 10 SDK; main at `31af725` plus this change | 2026-11-22 | Pass |

Requirement NFR-15 in the SE document still names Llama 3.1 and its "Built with Llama" notice; the model is now
K2 Horizon 7B, so the requirement's wording needs updating there.
