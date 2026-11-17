# UC 9 — Transcribe dictated audio

SwiftChart EHR · Samir Mukshe · CS 3354 Software Engineering, Fall 2026

The clinician dictates; the dictation sidecar (`src/Ehr.Dictation/sidecar`) transcribes it with MedASR
as they speak, and the on-prem app's dictation API (`src/Ehr.Web/Api/Dictation/DictationApi.cs`) passes the audio and
the transcript between the page and the sidecar.

## Test cases and their tests

The numbered test cases are the ones in the SE document (Assignment 4, sections 7.2 and 7.3), with the same IDs. Where a
case has more than one test, the first is the primary one; the others cover the same case at another layer.

| ID | Test case | Requirements | Tests |
|---|---|---|---|
| TC-UC9-01 | Successful flow: dictate and see the transcript as you speak | FR-07, FR-10, FR-32, NFR-14 | `src/Ehr.Dictation/sidecar/tests/test_sessions.py`: `test_chunks_in_order_build_a_live_transcript`; also `tests/Ehr.Web.Tests/DictationApiTests.cs`: `A_clinician_dictates_and_gets_the_fields_as_pending_proposals`, and `DictationPageTests.cs`: `The_page_token_drives_a_whole_dictation_to_pending_fields` |
| TC-UC9-02 | Alternative flow: cancel discards the dictation | FR-13 | `test_sessions.py`: `test_cancel_discards_audio_and_transcript` |
| TC-UC9-03 | Invalid input: audio that isn't audio, or can't be decoded, is refused | FR-10 | `test_sessions.py`: `test_bytes_that_are_not_audio_are_refused_as_not_audio`; also `test_api.py`: `test_bytes_that_are_not_audio_are_refused_at_stop`, and `DictationApiTests.cs`: `Anything_but_audio_is_refused_with_415`, `Audio_that_cannot_be_decoded_is_refused_with_422` |
| TC-UC9-04 | Exceptional condition: the connection drops and the transcript resumes | FR-07, FR-10 | `test_sessions.py`: `test_after_a_dropped_connection_the_transcript_resumes_where_it_left_off`; also `test_a_chunk_sent_again_is_ignored_and_one_that_skips_ahead_is_refused`, and `DictationApiTests.cs`: `A_chunk_out_of_order_is_answered_with_the_one_to_send_next` |
| TC-UC9-05 | NFR-07: dictated words never leave the team's server or reach the logs | NFR-07 | `test_logging.py`: `test_a_dictation_logs_none_of_its_words`; also `test_audio.py`: `test_nothing_is_written_to_disk`, and `DictationApiTests.cs`: `Nothing_is_saved_by_a_whole_dictation` |
| TC-UC9-06 | NFR-14: English dictation accuracy and live-transcript speed | NFR-14, FR-07 | Accuracy: `eval/tests/test_gate.py`: `test_a_score_below_the_accepted_baseline_fails`, and CI's "Check the committed eval results" step (`.github/workflows/ci.yml`), which checks `eval/results/latest.json`. Speed: `test_sessions.py`: `test_a_chunk_is_answered_in_time_while_medasr_is_busy_and_the_next_one_catches_up`, `test_stop_answers_within_its_budget_and_the_result_follows` |

Unprefixed `test_*.py` files are in `src/Ehr.Dictation/sidecar/tests/`, and unprefixed `*Tests.cs` files in
`tests/Ehr.Web.Tests/`.

## Limits

- TC-UC9-04 checks the sidecar's side of resuming: a chunk sent again is ignored, a chunk that skips ahead is told which
  to send, and the transcript carries on. The page's own retry after a dropped request isn't tested in a browser.
- TC-UC9-06's accuracy is the committed eval run, made on a developer Mac (it says so), not on the clinic server. The
  gate has no accepted baseline yet, so "no lower than the baseline" has nothing to compare with until one is accepted.
