# UC 6 — View test results

SwiftChart EHR · Simar Rekhi · CS 3354 Software Engineering, Fall 2026

## Patient workflow

Sign in to the portal and choose **Results** in the navigation, or **See your test results** on the portal home (`/portal/results`). Each released result shows the test, the date the sample was taken, the value with its unit, and whether it is low, normal or high. Open a result to read its normal range, what the result means in plain language, what the test measures, and any note from the care team. With no released results, the page says so and points the patient to their care team.

## Decisions

- **Who.** Patients only, through the Portal area's Patient policy. The page takes the patient from the session, never from the request: it has no ID parameter, which `PortalSubjectTests` checks for every portal handler.
- **Data.** One table, `ehr.observations`, shaped on FHIR's Observation (laboratory category): patient, LOINC code, display name, status, value, unit, reference range, sample time, release time (`issued_at`), the care team's note, and the lab order it answers (`order_id`, optional). The order link is keyed on `(order_id, patient_id)`, so a result can't name another patient's order. Check constraints hold the status to FHIR's codes and the range's low end at or below its high end.
- **Released.** A patient sees a result only once it has a release time and its status is `final`, `amended` or `corrected`. The rule is in the row-level security policy (`observations_own_released_select`) and repeated in `PhiReader.ResultsAsync`.
- **Database access.** Row-level security is enabled and forced. Clinicians read every result, as for the rest of the chart; a patient reads only their own released ones. Grants: `ehr_app` and `ehr_read` SELECT only. Nothing at runtime writes results yet. The standard write-audit trigger records any write.
- **Reads.** Through `PhiReader.ResultsAsync`. Opening the page is one recorded read of the patient, as for the chart. `CloudAuditEvents` did not need widening.
- **Off-site.** Like every page that reads PHI, the results page is served from the cloud replica when the on-site server is down. It shows the read-only banner, and its read goes to the cloud audit queue and arrives on-site with source `cloud`.
- **Plain language (FR-16).** `Results/ResultExplanation.cs` flags the value against its range, writes a range with one open end the way people say it ("below 100 mg/dL"), drops the zeros the database keeps, and says what 14 common tests measure, by LOINC code.
- **Seed.** The synthetic clinic gives each patient up to six results from 13 common tests after their visits. 90% are released, and some results outside their range carry a care-team note. Seeded results don't link to orders, since the seed has none.

## Courses of events and their tests

In `tests/Ehr.Web.Tests/PortalResultsTests.cs` and `ResultExplanationTests.cs`:

| Course of events | Test |
|---|---|
| Typical: released results, newest first, each with its range and meaning; one recorded read | `A_patient_sees_their_released_results_newest_first_in_plain_language` |
| No results yet | `A_patient_with_no_released_results_is_told_so` |
| Signed out or a clinician | `Nobody_signed_out_and_no_clinician_reaches_the_results_page` |
| On-site server down: read-only, the read queued and shipped | `Off_site_with_the_clinic_server_down_results_are_read_only_and_the_read_is_queued` |
| Low, normal and high against both ends | `A_value_is_compared_with_both_ends_of_its_range` |
| A range with one end | `A_range_with_one_end_reads_as_people_say_it` |
| Numbers without trailing zeros | `Numbers_drop_the_zeros_the_database_keeps` |
| No number or no range | `Without_a_number_or_a_range_the_patient_is_sent_to_their_care_team` |
| What the test measures | `Known_tests_say_what_they_measure_and_unknown_ones_say_nothing` |

In `tests/Ehr.Security.Tests`:

| Rule | Test |
|---|---|
| A patient sees a result only once released and finished, through both roles | `A_patient_sees_their_result_only_once_it_is_released_and_finished` |
| A result answers only its own patient's order | `A_result_answers_only_its_own_patients_order` |
| Grants, protection, policies, audit trigger and RLS for the table | the `observations` rows in `RuntimeRoleTests`, `ProtectionTests`, `PolicyPlanTests`, `WriteAuditTests` and `RowLevelSecurityTests` |

The render benchmark measures "Portal results, the patient's own" against NFR-01.

## Human review

These need line-by-line review by someone other than Simar (AGENTS.md):

- `db/policies/10-grants.sql`, `30-rls.sql` and `40-audit.sql`: the grants, the two policies and the audit trigger for observations.
- `PhiReader.ResultsAsync`.

## Limits

- Nothing writes results yet: they will come from a lab interface, which isn't built. The seed and the tests insert them directly.
- The portal doesn't show which order a result answers, and the clinician's chart doesn't list results yet.
- Proxy access (FR-19), asking the care team about a result (FR-18) and booking a follow-up (FR-17) aren't built.
