# UC 1 — Place an order

SwiftChart EHR · Tahseen Arnan · CS 3354 Software Engineering, Fall 2026

## Clinician workflow

Open a patient on your panel and choose **New order** on the chart, or press Alt+Shift+O. The order form shows the patient's name and allergies at the top. Pick a type: lab, imaging, medication or referral. Type the item; matching items from that type's catalog are suggested as you type. Fill in the type's details; required ones are marked.

**Sign order** checks for missing fields and for an active order of the same item, saves the order on the on-site primary, and takes you back to the chart, which confirms the order and lists it under **Active orders**. **Cancel** goes back to the chart and saves nothing.

| Type | Required | Optional |
|---|---|---|
| Lab | Reason for the test | Notes for the lab |
| Imaging | Reason for the exam | Notes for imaging |
| Medication | Dose, route, frequency | Duration |
| Referral | Reason for referral | Notes for the specialist |

## Decisions

- **Who.** Clinicians only, and only for a patient on their panel, as for "New note" and dictation. Under emergency access the chart says to add the patient to the panel first, and the order page sends anyone off the panel back to the chart before anything is read.
- **Data.** One table, `ehr.orders`: the patient, type, catalog item, the type's details (a JSON object), status, signer, signing time and duplicate reason. The database sets the signer (the signed-in user, which row-level security holds it to) and the signing time (the clock's, in the signing trigger), as for signed notes.
- **Append-only.** There is no UPDATE or DELETE grant on orders, and the trigger refuses any status but `active`. Discontinuing or completing an order is not built.
- **Catalog.** `ehr.order_catalog_items`: 29 synthetic items across the four types, seeded by the `Orders` migration. The codes are plain synthetic ones (`SYN-LAB-001`, …), because the synthetic clinic records its medications without codes. No licensed code set is used. The catalog is not PHI.
- **Suggestions.** A `datalist` of the type's items. The order page turns htmx off, since its address carries the patient.
- **Duplicates.** An active order for the same patient and catalog item. The page warns and asks for a reason. The database enforces it: the signing trigger (`db/policies/60-orders.sql`) locks the patient's row, then refuses a second active order for the item unless a reason is given, so two submissions at once can't both get through. The trigger refuses any transaction that isn't READ COMMITTED, because at a stricter level the waiting transaction would check against an old snapshot.
- **Database access.** Row-level security is enabled and forced. A clinician inserts only as themselves and only for a patient on their active panel. Reads follow the `clinical_notes` policy: any clinician. Grants: `ehr_app` SELECT and INSERT; `ehr_read` SELECT. The standard write-audit trigger records each order with the clinician and the time.
- **Change log.** No change-log trigger: the desktop app's chart cache doesn't hold orders, so nothing needs their changes today.
- **Reads.** Through `PhiReader`. The order form's banner and allergies are one recorded read. The chart's active-orders list loads only the fields it shows and is part of the chart's one recorded read, with no audit row per order. `CloudAuditEvents` did not need widening: both reads are recorded against `patients`, as the chart's already was.
- **Off-site.** The cloud app forwards the post to the on-site server. `js/orders.js` posts the form with `fetch`; on a 503 or a network failure the form stays on screen with its entries and says nothing was saved. Without JavaScript the form posts normally. While the site is read-only the chart offers no "New order", as for "New note".
- **Shortcut.** Alt+Shift+O, set in `aria-keyshortcuts`. It is clear of "/", Ctrl/Cmd+S and dictation's Alt+Shift+D.
- **FR-14.** `DictationOrdersTests` scans the dictation code and fails if any of it names a type from the order code.

## Test cases, courses of events and their tests

The Assignment 4 test cases are one file each, in `tests/Ehr.Web.Tests/TestCases/UC1/`. Each file's header holds the test case's fields: ID, purpose, type, requirements, preconditions, inputs, test data, steps, expected result and creator.

| Test case | What it checks | File |
|---|---|---|
| TC-UC1-01, successful flow | A clinician signs a lab order; the chart confirms and lists it; the audit row names the clinician and the time | `TC_UC1_01_SuccessfulFlow.cs` |
| TC-UC1-02, alternative flow | A match with an active order warns; saved only once a reason is given | `TC_UC1_02_AlternativeFlow.cs` |
| TC-UC1-03, invalid input | An empty required field is highlighted, nothing is saved, the entries stay | `TC_UC1_03_InvalidInput.cs` |
| TC-UC1-04, exceptional condition | Off-site with the on-site server unreachable, the order isn't saved and the form keeps its entries | `TC_UC1_04_ExceptionalCondition.cs` |
| TC-UC1-05, NFR-01 | On-site, the chart with active orders and the order form: p99 under 15 ms, the commit included, at 50,000 patients with durability on | `TC_UC1_05_Nfr01_OnSiteNavigation.cs` |
| TC-UC1-06, NFR-12 | Keyboard-first: the shortcut, autofocus, labelled fields and real buttons, signing without a mouse | `TC_UC1_06_Nfr12_KeyboardFirst.cs`, and its manual browser walkthrough, `TC_UC1_06_Nfr12_KeyboardFirst.md` |

TC-UC1-05 runs only against a run of the render benchmark; `tests/Ehr.Web.Tests/TestCases/PerformanceResults.cs` says how. In the normal test run it is skipped.

Extra coverage, without test case IDs, in `tests/Ehr.Web.Tests/OrderTests.cs`:

| Course of events | Test |
|---|---|
| A role that can't order sees no "New order" and is refused | `A_role_that_cannot_order_sees_no_new_order_and_is_refused` |
| Off-site, the order is forwarded and saved on-site | `Off_site_the_order_is_forwarded_and_saved_on_site` |
| Cancel saves nothing | `Cancel_saves_nothing` |

In `tests/Ehr.Security.Tests/OrderSecurityTests.cs`, against the database directly:

| Rule | Test |
|---|---|
| The database sets the time; the audit row names the clinician and the time | `The_database_sets_the_signing_time_and_the_audit_row_names_the_clinician_and_the_time` |
| An admin, a patient, a disabled clinician, nobody and the read role can't insert | `Inserts_by_an_admin_a_patient_a_disabled_clinician_nobody_or_the_read_role_are_refused` |
| Signing as someone else | `Signing_as_someone_else_is_refused` |
| A patient off the panel | `An_order_for_a_patient_off_the_clinicians_panel_is_refused` |
| No updates or deletes | `Orders_cannot_be_updated_or_deleted` |
| A duplicate needs a reason | `A_duplicate_active_order_without_a_reason_is_refused_and_one_with_a_reason_is_kept` |
| Two duplicates at once | `Two_duplicates_at_once_cannot_both_get_through` |
| Only READ COMMITTED | `An_order_outside_a_read_committed_transaction_is_refused` |
| Only active orders | `An_order_that_is_not_active_is_refused` |
| The item is of the order's type | `An_order_names_a_catalog_item_of_its_own_type` |
| Who reads orders | `Clinicians_read_every_order_and_nobody_else_reads_any` |

## Human review

These need line-by-line review by someone other than Tahseen (AGENTS.md):

- `db/policies/60-orders.sql`: the signing trigger, with its isolation check, the patient row lock, the status check, the duplicate check and the signing time.
- `db/policies/10-grants.sql`, `30-rls.sql` and `40-audit.sql`: the grants, the two policies and the audit trigger for orders.
- The order page's panel rule (`Areas/Clinical/Pages/Patients/Orders/New.cshtml.cs`) and the chart's condition for offering "New order".
- The new `PhiReader` methods. `CloudAuditEvents` is unchanged.

## Limits

- Orders can't be discontinued, completed or corrected. Every order stays active.
- An order isn't sent anywhere: no lab, pharmacy or referral interface.
- The catalog is small and synthetic, and changing it takes a migration.
- A medication order isn't checked against the patient's allergies; the form shows them for the clinician to read.
- The form's behaviour with `fetch` in a browser is checked by hand; the tests cover the server side.
- With `--stack`, the render benchmark leaves its 400 orders on the primary, as it leaves its audit events.
