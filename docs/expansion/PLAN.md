# SwiftChart expansion plan

An experimental line of work on `expansion/integration`, building SwiftChart out into a complete EHR. Nothing here reaches main until Tahseen merges it back himself, phase by phase, through reviewed pull requests.

At the start of every session, read this file, its status table and both logs, then resume at the first step that isn't done. AGENTS.md wins wherever it conflicts with this file.

Under each step, "In the repo" says what the step touches as the code stood when the plan was checked against it (2026-11-18, main at `b4ad35b`), and what already exists. Check it again when the step starts: earlier steps change it.

## Status

| Step | What | Status | Branch | Tag | CI run | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| 0 | This plan | done | expansion/step-00-plan | expansion-step-00 | [44257188518](https://github.com/tarnan3751/EHR-Electronic-Health-Record-Project/actions/runs/44257188518) | |
| 1 | Change and discontinue orders | done | expansion/step-01-order-changes | expansion-step-01 | [44263160924](https://github.com/tarnan3751/EHR-Electronic-Health-Record-Project/actions/runs/44263160924) | UC 1 (Tahseen). Touches `db/policies` |
| 2 | Editable allergies, medications, diagnoses | done | expansion/step-02-chart-lists | expansion-step-02 | [44271858512](https://github.com/tarnan3751/EHR-Electronic-Health-Record-Project/actions/runs/44271858512) | UC 4 (Diego), UC 7 (Sara), and the banner on UC 9, 13 and 14's pages (Samir, Satwik). Touches `db/policies`. Notes can't be marked sensitive yet: see the log |
| 2a | Reference data and a coherent synthetic seed | blocked | expansion/step-02a-reference-data | | | Waiting for Tahseen: a loinc.org account for LOINC; which drug-class source (MED-RT, or FDA classes through RxClass); which interaction list. Nothing built yet on the branch |
| 3 | Clinician inbox | not started | | | | |
| 4 | Appointments | not started | | | | |
| 5 | Secure messaging | not started | | | | |
| 6 | Proxy access | not started | | | | |
| 7 | Order → result → acknowledgment | not started | | | | |
| 8 | Ordering safety checks | not started | | | | |
| 9 | Vital signs and trends | not started | | | | |
| 10 | Addenda to signed notes | not started | | | | |
| 11 | Read-only FHIR API | not started | | | | |
| 12 | Simulated lab | not started | | | | |
| 13a | Visit summaries | not started | | | | |
| 13b | Download my record | not started | | | | |
| 13c | Who viewed my record | not started | | | | |
| 13d | Refill requests | not started | | | | |
| 13e | Pre-visit forms | not started | | | | |
| 14 | SOAP notes and templates | not started | | | | |
| 15 | Dictation proposals beyond the note | not started | | | | asks first |
| 16 | Ambient listening | not started | | | | asks first |
| 17 | Languages beyond English | not started | | | | asks first |
| 18 | Session timeouts, emergency-access review | not started | | | | |
| 19 | Passkeys | not started | | | | |
| 20 | Desktop distribution | not started | | | | part asks first |
| 21 | Real deployment and operations | not started | | | | asks first |

Statuses: not started, in progress, done, blocked (with the reason in Notes).

## Branch workflow, for every step

1. **Bring main in first.** `git switch expansion/integration`, `git pull --ff-only`, `git fetch origin`, `git merge origin/main` (a merge, never a rebase), then push. If main gained a database migration, don't hand-merge the model snapshot: recreate this line's own new migrations after main's, and regenerate `db/migrations/schema.sql`.
2. **Branch.** Create `expansion/step-NN-short-name` from `expansion/integration`.
3. **Work.** Make small commits. Push the step branch so CI runs on it. Never open a pull request. (CI runs on every push to any branch, `.github/workflows/ci.yml`; its image-publishing job runs on main only.)
4. **Merge back.** When the step is done and its CI run is green: `git switch expansion/integration`, `git merge --no-ff <step branch>`, `git tag expansion-step-NN`, then push the branch and the tag.
5. **Record it.** Update this file's status table and log on the step branch, before merging. (The CI run recorded is the one for the step's last commit before that record; see the decision log.)
6. **At the end of a phase:**
   - tag `expansion-phase-N` and push a branch `expansion/phase-N` at that tag;
   - write the pull request title and description in the "Phase merges" section, including the protected paths the phase touched;
   - tell Tahseen, then keep going. He opens that pull request and gets it reviewed himself.

## Ground rules

- **Main is off limits.** Never push to main, merge into main, open a pull request, or change repo settings.
- **Ask Tahseen before** any force-push, rewriting history, or deleting a branch or tag.
- **Git habits.** Never `git stash`. Stage files by name; never `git add -A` or `git add .`. Leave `.claude/` alone and never commit it.
- **Files.** LF line endings. No bash scripts in the repo: tooling is `dotnet run script.cs` or PowerShell 7, as AGENTS.md says.
- **Data and secrets.** Synthetic data only. Never commit secrets, model weights, signing keys, `.env` files or `test-accounts.json`. Read test passwords and authenticator codes at runtime, and never write them to files or logs.
- **Scratch work.** Throwaway scripts, screenshots and notes go in a scratch folder outside the repo, and are never committed.

## Every step must meet these

**The brand kit**
- **Follow the guide.** All UI follows `docs/brand/README.md`. `docs/brand/tokens.json` is the source of truth for color, type, spacing, radius and size; after changing tokens, the shared stylesheet, a font or a logo, run `dotnet run tools/brand/sync.cs`.
- **Use the existing styles.** Use the guide's `sc-` components and the token variables only, never raw colors or one-off styles. A new component goes into the shared stylesheet in the guide's style, with a note in `docs/brand`.
- **Make it look and work right.**
  - Both themes must be equally finished, with WCAG AA contrast.
  - Every interactive element shows the focus ring, and reduced motion is respected.
  - Clinician screens are keyboard-first (NFR-12).
  - The portal uses its own sizes: 18px text, 44px controls, one column, working down to 360px wide.
- **Write in the guide's voice:**
  - sentence case;
  - notices start with their state word ("Read-only:");
  - buttons name their action;
  - dictation output is "proposed", never "decided";
  - mono type for MRNs and IDs.
- **Check against the kit.** Look at every new or changed screen in light and dark, and compare it with the kit. If `~/Downloads/swiftchart-brand-kit/components/index.html` is still there, use its reference renders. (It was there on 2026-11-18.)

**Performance (the course document and CI)**
- **Speed targets:**
  - NFR-01: on-site page navigation under 15 ms;
  - NFR-02: desktop navigation under 5 ms;
  - NFR-03: off-site navigation in one network round trip plus under 20 ms;
  - NFR-04: all of them holding with 50,000 synthetic patients.
- **CI gates:**
  - p99 server render under 20 ms, excluding the commit, at 50,000 patients (`bench/Ehr.Bench`);
  - page JavaScript under 50 KB compressed, as served;
  - first contentful paint under 500 ms.
  (Only the first is checked by CI today. AGENTS.md lists the other two as gates to add; until a step adds them, measure both by hand for each step and hold them as if CI did. See the decision log.)
- **The benchmark must cover new work.** Add every new page to the render benchmark, and extend the synthetic seed (`bench/Ehr.Seed`) with every new kind of data, so the 50,000-patient numbers include it.
- **Measure and record.** Record p99, page JavaScript size and first contentful paint in the log for each step that changes pages.
- **Don't add weight.** No new JavaScript or CSS framework, no CSS build step, and htmx stays. Draw charts as server-rendered SVG, not with a charting library.
- **Work offline.** Nothing is loaded from another host: everything on-site keeps working without internet (NFR-08).

**Security and data**
- **Every new table** gets:
  - a protection decision (`ExpectedProtection` in `tests/Ehr.Security.Tests/ProtectionTests.cs`);
  - for patient data, row-level security enabled and forced (`db/policies/30-rls.sql`), plus the write-audit trigger (`db/policies/40-audit.sql`);
  - explicit grants in `db/policies/10-grants.sql` and the same privileges in `ExpectedPrivileges`.
- **The existing rules still hold:**
  - permissions are checked in both the app and the database;
  - patient data is read only through the PHI reader (`src/Ehr.Domain/Phi/PhiReader.cs`), which logs every read;
  - portal handlers never accept a patient ID (`tests/Ehr.Architecture.Tests/PortalSubjectTests.cs`);
  - every `SECURITY DEFINER` function sets `search_path` and revokes EXECUTE from PUBLIC.
- **Prove each new rule** with security tests, and prove that everyone else's isolation still holds.
- **Sensitive items stay protected.** Diagnoses, medications and notes marked sensitive (step 2) are never exposed to proxies without the patient's explicit permission, carry their own audit marker, and are shown as `docs/brand` describes. (`docs/brand/README.md` doesn't describe sensitive items yet: step 2 adds that section before it builds the screens.)

**The other surfaces**
- **Off-site:** reads work through the cloud copy and changes are forwarded (`src/Ehr.Web/Forwarding`). While the clinic's server is unreachable, the new feature's save actions are hidden with an explanation. Add the feature to the off-site tests (`OffSiteOutageTests.cs`, `CloudForwardingTests.cs`, and `ShownIfNotSaved` for free text a person typed).
- **Desktop:** if a feature adds chart data, decide whether the offline copy needs it, update the offline copy (`/api/desktop/offline-panel`, `PhiReader.OfflinePanelAsync`, `src/Ehr.Desktop/src-tauri/src/charts.rs`, `frontend/app.js`), and keep NFR-02.

**Tests and docs**
- **Test cases:**
  - extending an existing use case continues its test-case numbering;
  - a new use case gets four functional test cases (successful flow, alternative flow, invalid input, exceptional condition) and two non-functional ones, one file each, in `tests/Ehr.Web.Tests/TestCases/UCnn/`;
  - browser tests walk the keyboard path.
- **Docs:**
  - update `docs/UCnn.md` for each new or changed use case (today only UC 1, 2, 6, 9, 10, 12, 13 and 14 have one: create the file when a step first changes another use case);
  - propose requirement IDs (FR-37 onward, UC 15 onward) in "Requirements to propose";
  - keep the README's description of what's built current.
- **Protected paths:** if a step touches any path AGENTS.md puts under line-by-line review (`db/policies`, authorization attributes and policies, the sync conflict resolver, the dictation prompt and note schema), say so in its commit messages and in the phase's pull request draft.

**Done, for each step**
- `dotnet format --verify-no-changes`, the Release build and the full `dotnet test` pass locally;
- the render benchmark's `--assert` passes;
- `cargo fmt --check` passes when the desktop app changed;
- you've looked at the screenshots in both themes;
- CI is green on the step branch;
- the log entry is written.

## Reference data and synthetic data

**How every code list is handled**
- Each list is loaded as data by a committed `dotnet run` import script, from a pinned source version.
- Its license and attribution are recorded in `docs/expansion/reference-data.md`.
- Nothing is fetched at runtime: on-site keeps working without internet (NFR-08).
- Ask Tahseen before using any source whose license isn't clearly free (NFR-16), and before signing up for anything (for example a UMLS account for SNOMED CT).

**Sources**
- **Drugs and drug allergies:** RxNorm's prescribable content, which NLM publishes without a license.
- **Drug classes, for allergy cross-checks** (a penicillin allergy against an amoxicillin order): MED-RT, distributed by NLM. Confirm its license first.
- **Non-drug allergens** (foods, latex, insect stings, environmental): the FDA's UNII substance list, which is public domain. Add a short curated list of reactions (hives, anaphylaxis, rash and so on) and FHIR's criticality and severity values.
- **Conditions, physical and mental:** ICD-10-CM, which is public domain, including chapter F (mental, behavioral and substance-use disorders).
- **Lab tests:** LOINC (free license, attribution required), using LOINC's Universal Lab Order Codes for the orderable catalog.
- **Units:** UCUM.
- **Vital signs:** LOINC's vital-signs codes, with UCUM units.
- **Reference ranges:** a curated table, labeled as this clinic's ranges, noting the source of each range.
- **Immunizations (optional):** CDC's CVX vaccine codes.
- **Screening questionnaires:** PHQ-9 (depression) and GAD-7 (anxiety), which are free to use, with their LOINC codes.
- **No SNOMED CT or CPT** unless Tahseen approves: both need licenses.

**Synthetic patients**
- The seed is clinically coherent: each patient's conditions come with matching medications, allergies, labs, vitals and visits. For example, a patient with type 2 diabetes is on metformin and has HbA1c results.
- Evaluate Synthea (Apache 2.0) as the generator, or as the model for the seed's own generator. Synthea codes conditions in SNOMED CT, so either map them to ICD-10-CM or ask Tahseen about a UMLS license.
- Whatever generates the data, the 50,000-patient seed keeps `bench/Ehr.Seed`'s rules from AGENTS.md and stays fast enough for CI.

**In the repo today**
- `bench/Ehr.Seed/SyntheticClinic.cs` and `Seeder.cs` generate patients with allergies, medications, diagnoses, visits and test results; `tests/Ehr.Security.Tests/SeedTests.cs` checks the seed's rules.
- The chart tables already have nullable `code` columns, with comments naming the intended code system: ICD-10-CM for `conditions`, RxNorm for `medication_statements`, LOINC for `observations`, and SNOMED CT for `allergy_intolerances`, whose "No known allergies" row uses SNOMED CT code 716186003 (`AllergyIntolerance.NoKnownAllergy`). Step 2 moves allergies off SNOMED CT (see the decision log).
- The order catalog (`ehr.order_catalog_items`, 29 items) uses the app's own `SYN-...` codes, seeded by its migration through `HasData`.
- There are no reference-data tables, import scripts or `docs/expansion/reference-data.md` yet.

## Stop and ask Tahseen before

- spending money or creating any account: hosting, Apple Developer, code-signing certificates, domains, UMLS;
- changing an existing requirement (an FR or NFR) or a documented use case's behavior beyond adding to it (steps 15 and 17 do both);
- adding an AI model, a dataset, a code list, or a dependency whose license isn't clearly free and open (NFR-15, NFR-16), or downloading new model weights;
- committing any recording of a voice;
- anything on main, a force-push, deleting a branch or tag, or changing repo settings;
- meeting a performance or security gate only by cutting scope;
- CI failing for a reason two honest attempts haven't fixed.

When you stop, and also whenever this session's context is running low:
- finish or commit the work in progress;
- push it;
- update the status table and the log, saying exactly where to resume;
- give Tahseen a short summary.

## Use-case owners

These are from the course document. When a step changes someone's area, list them in the step's notes, so Tahseen can give them a heads-up.

| Use cases | Owner | Area |
| --- | --- | --- |
| UC 1, UC 2 | Tahseen Arnan | orders, off-site outage |
| UC 3, UC 4 | Diego Valenzuela | search, chart |
| UC 5, UC 6 | Simar Rekhi | portal registration, results |
| UC 7, UC 8 | Sara Lelo | desktop offline, sync |
| UC 9, UC 10 | Samir Mukshe | dictation service |
| UC 11, UC 12 | Luke Garcia | sign-in, emergency access |
| UC 13, UC 14 | Satwik Yash Padhy | AI review, notes |

## Phases and steps

### Phase 1: finish the specified scope, plus the foundations

**Step 1. Change and discontinue orders** (UC 1, FR-05)

If step 1's own prompt gave more detail, that detail wins.
- **A signed order is never edited in place.**
  - Discontinue records a status of `discontinued`, a required reason, who and when. (Stored as FHIR's request status `revoked`, which the `orders` status constraint already allows, and shown as "Discontinued"; see the decision log.)
  - Change places a new order and discontinues the old one as replaced, in one transaction, linked both ways.
  - Nothing reactivates an order or changes any other column.
- **The database enforces it.** Use the narrowest privilege that works (a column-level UPDATE grant or a `SECURITY DEFINER` function), an RLS policy limited to clinicians who can already order for that patient, and the write-audit trigger.
- **The duplicate check** applies to a replacement but ignores the order it replaces. Dictation still never touches orders (FR-14).
- **Chart screens:**
  - "Change" and "Discontinue…" on each active order, usable from the keyboard;
  - a view of discontinued orders with who, when and why;
  - off-site, these actions are hidden with an explanation.
- **Touches:** `db/policies`, which needs line-by-line review.
- **In the repo:**
  - *Exists:* `ehr.orders` (`src/Ehr.Data/Orders/Order.cs`, `OrderConfiguration.cs`), append-only, always `active`, with `SELECT, INSERT` for `ehr_app`; `orders_clinician_select` and `orders_own_insert` (a clinician, signing as themselves, for a patient on their active panel) in `db/policies/30-rls.sql`; the `ehr.sign_order()` trigger in `db/policies/60-orders.sql` (patient-row lock, active only, duplicate check, signing time); `src/Ehr.Domain/Orders/ClinicalOrders.cs` and `OrderForms.cs`; `PhiReader.OrderFormAsync` and the chart's active orders in `PhiReader.ChartAsync`; the order form `Areas/Clinical/Pages/Patients/Orders/New.cshtml(.cs)` with `js/orders.js`; the chart's "Active orders" section in `Patients/Chart.cshtml`.
  - *New:* columns on `orders` for the discontinuation (reason, who, when) and the two links (`replaces_order_id`, `replaced_by_order_id` or one of them with a unique index), in an EF migration with `schema.sql` regenerated; the discontinue path in `60-orders.sql`, its grant in `10-grants.sql` and `ExpectedPrivileges`; `ClinicalOrders.DiscontinueAsync` and `ChangeAsync`; pages `Orders/Discontinue` and `Orders/Change` (or `New` with `?replaces=`); the chart's discontinued-orders view; the bench's seed and render rows for them.
  - *Tests:* `tests/Ehr.Security.Tests/OrderSecurityTests.cs`, `tests/Ehr.Web.Tests/OrderTests.cs`, `OffSiteOutageTests.cs`, `tests/Ehr.Architecture.Tests/DictationOrdersTests.cs` (must still pass untouched), and `TestCases/UC1/` continuing at TC-UC1-07. `docs/UC1.md` and the README's "Orders can't be changed or discontinued yet".

**Step 2. Editable allergies, medications and diagnoses, with standard codes**
- **Editing:** clinicians add, edit and mark inactive (never delete) allergies (with reaction and criticality, drug and non-drug), medications, and diagnoses as a problem list. History is kept and every change is audited.
- **Codes:** from "Reference data and synthetic data". If step 2a hasn't loaded them yet, start with the minimal subsets this step needs and leave the full import to 2a.
- **Search:** keyboard-first type-ahead search for codes, within the performance budget.
- **Sensitive items:**
  - mark mental-health, substance-use and other sensitive diagnosis categories (by ICD-10-CM range) as sensitive, and allow marking individual items sensitive;
  - record the rule in the decision log;
  - show sensitive items as `docs/brand` describes, so they're never exposed by accident.
- **Banner:** the patient banner shows allergies on every patient page wherever the read stays audited and within the render budget.
- **Offline:** the desktop app's offline copy includes these lists, read-only.
- **Dictation still never changes allergies** (FR-14).
- **Touches:** `db/policies`. **Affects:** UC 4, UC 7.
- **In the repo:**
  - *Exists:* `ehr.allergy_intolerances`, `ehr.medication_statements` and `ehr.conditions` (`src/Ehr.Data/Patients/`), each PHI with forced RLS and the write-audit trigger, `SELECT` only for both runtime roles, with FHIR status columns and a nullable `code`; the chart reads them in `PhiReader.ChartAsync` (`src/Ehr.Domain/Phi/PatientChart.cs`) and shows what the patient has now; the portal home shows the patient's own; the patient banner (`Pages/Shared/PatientBanner.cs`, `_PatientBanner.cshtml`) shows allergies only on the chart and the order form, because the notes, dictation and review pages don't read them; the desktop's offline chart already carries the three lists as the chart shows them.
  - *New:* write grants and RLS write policies for clinicians (panel rule, as for orders); history (a row per version, or a history table written by trigger); `sensitive` on conditions, medications and notes with the ICD-10-CM range rule; the reference tables this step needs (or 2a's); a type-ahead endpoint and its small script; edit forms on the chart; a "Sensitive items" section in `docs/brand/README.md` and its component in `ehr.css`; replacing the SNOMED CT "No known allergies" code.
  - *Tests:* `PatientChartTests.cs`, `PatientWriteTests.cs`, `RowLevelSecurityTests.cs`, `WriteAuditTests.cs`, `ProtectionTests.cs`, `SeedTests.cs`, `TestCases/UC4/` continuing its numbering, the desktop's Rust tests. `docs/UC4.md` doesn't exist yet.

**Step 2a. Reference data and a coherent synthetic seed**
- Load every source in "Reference data and synthetic data" with its import script, and write `docs/expansion/reference-data.md`.
- Upgrade the seed so all 50,000 patients have coherent coded conditions, medications, allergies, labs, vitals and visits, with a realistic share of sensitive items.
- Keep the benchmark and CI times in budget, and record seed size and seeding time in the log.
- Steps 2, 8, 9, 12 and 13e build on this.
- **In the repo:** see "In the repo today" under "Reference data and synthetic data". *New:* reference tables (not PHI: grants only, as `order_catalog_items`), import scripts under `tools/`, the pinned source files or their checksums, and the order catalog's move from `SYN-...` codes to LOINC and RxNorm where they apply. Decide where the loaded rows live so that tests, the bench and the stack all have them without the internet (see the decision log when made).

**Step 3. Clinician inbox**
- One inbox per clinician for items that need action. Item types plug in: messages (step 5), results to acknowledge (step 7), refill requests (13d), and emergency-access reviews for admins (step 18).
- A count in the app bar, keyboard navigation, and each item linking to its patient and source.
- It starts empty; each later step adds its own item type.
- **In the repo:** nothing exists. *New:* an inbox table (PHI: it names patients), a page under `Areas/Clinical/Pages/Inbox`, the app bar link and count in `Pages/Shared/_Layout.cshtml` (the count is a read on every clinical page: keep it inside the render budget, and off the pages `PatientSearchTests` holds to an exact list of requests), `LayoutTests.cs`, a new use case with its six test cases, and bench rows.

**Step 4. Appointments** (FR-17)
- **Scheduling:** staff create, reschedule and cancel appointments, with a day view per clinician, linked to visits.
- **Portal:** patients see their upcoming and past appointments. There's no self-scheduling yet.
- **Placeholder:** replace the portal's "Appointments and messages will appear here." as appointments and messages arrive.
- **Affects:** UC 6.
- **In the repo:** `ehr.encounters` holds visits (FHIR Encounter statuses, including `planned`), read-only at runtime; the placeholder is at `Areas/Portal/Pages/Index.cshtml`; `docs/UC6.md` lists appointments (FR-17), messages (FR-18) and proxy access (FR-19) as what's missing from UC 6. *New:* an appointments table, clinical schedule pages, the portal's list, seed and bench rows, `TestCases/UC6/` continuing at TC-UC6-07.

**Step 5. Secure messaging** (FR-18)
- Threads between a patient and their care team. The clinician side runs through the inbox, and every read is audited.
- The notification email says only that there's a new message and links to the portal. It contains no health details.
- **Affects:** UC 6.
- **In the repo:** email goes through `IEmailSender.SendAsync(to, subject, body, EmailAction, …)` (`src/Ehr.Domain/Accounts/IEmailSender.cs`, `src/Ehr.Web/Email/`); the care team is the clinicians with the patient on their panel (`ehr.patient_assignments`). *New:* thread and message tables, portal pages, the inbox item type, the off-site "messages cannot be sent until the server is back" wording `docs/UC6.md` already names.

**Step 6. Proxy access** (FR-19)
- **The relationship:** a proxy relationship, recorded by the clinic or granted by the patient, has a scope and an expiry.
- **Acting for someone:** the proxy's session chooses whose record they're acting for. Every portal page, query and RLS policy uses that person, and the audit log records both the proxy and the patient.
- **Sensitive items:** proxies don't see sensitive items (step 2) unless the patient explicitly allows it, and that permission is enforced in the database and audited.
- **This changes the rule that a patient sees only their own row.** Update RLS, `PortalSubjectTests`, `PortalResultsIsolationTests` and the security tests deliberately, and prove isolation still holds for everyone else.
- **Touches:** `db/policies` and authorization. **Affects:** UC 5, UC 6, UC 11.
- **In the repo:** the portal's subject is `ehr.current_patient_id()` (`db/policies/30-rls.sql`), set from the session by `UserContextMiddleware` and `UserContextInterceptor`; every `*_own_select` policy keys on it; `PortalSubjectTests` is in `tests/Ehr.Architecture.Tests`, `PortalResultsIsolationTests` in `tests/Ehr.Security.Tests`.

At the end of phase 1, every FR in the course document is built. Write the phase pull request draft. Also note that this is a good time for step 21's deployment, but don't start it without Tahseen.

### Phase 2: close the clinical loops

**Step 7. Order → result → acknowledgment**
- Results attach to the order that requested them, and the order moves to resulted.
- The ordering clinician gets an inbox item. Acknowledging it records who and when.
- Abnormal results, judged against step 2a's reference ranges, are flagged and sorted first. The portal keeps showing results as it does now.
- **Affects:** UC 1, UC 6.
- **In the repo:** `ehr.observations` already has `order_id` (same patient enforced), `reference_low`/`reference_high` and `issued_at`; nothing at runtime writes observations. "Resulted" is FHIR's `completed` in `OrderStatus`. The portal's results are `PhiReader.ResultsAsync`, `Results/ResultExplanation.cs` and `Areas/Portal/Pages/Results.cshtml`.

**Step 8. Ordering safety checks**
- **The checks:** a medication order is checked against the patient's coded allergies, including drug-class cross-sensitivity from step 2a's class data, and against active medications for known interactions.
- **The warning** names the problem. Overriding it requires a reason, which is recorded and audited.
- **The data** stays local, with no outside service (NFR-08). For interactions, use a small curated set, such as the published ONC high-priority list. Check each dataset's license, and ask Tahseen if it's unclear.
- **Affects:** UC 1.
- **In the repo:** the order form already loads the patient's allergies for its banner (`PhiReader.OrderFormAsync`) and has the duplicate warning with a required reason (`duplicate_reason`), the pattern the override follows.

**Step 9. Vital signs and trends**
- **Recording vitals:** blood pressure, heart rate, temperature, respiratory rate, SpO2, weight and height, with BMI calculated. They're stored as observations with LOINC codes and UCUM units, and shown in the chart as a flowsheet.
- **Trend charts:**
  - for vitals and lab values, on the chart and on the portal's results;
  - server-rendered SVG, colored with tokens;
  - with each reference range shown;
  - each with a data table as the accessible alternative.
- **Affects:** UC 4, UC 6.
- **In the repo:** blood pressure and heart rate are columns of `ehr.encounters` today (`SystolicMmHg`, `DiastolicMmHg`, `HeartRatePerMinute`), shown with the chart's latest visit and in the desktop's offline chart; `ehr.observations` holds lab results only and has no category column. This step moves vitals to observations and migrates those columns.

**Step 10. Addenda to signed notes**
- Signed notes stay locked. An addendum is a new signed text attached to the note, shown in order with its author and time, and audited.
- The desktop app's offline view shows addenda too.
- **Affects:** UC 14.
- **In the repo:** `ehr.clinical_notes` is insert-only, with `ehr.reject_clinical_note_change()` in `db/policies/50-clinical-notes.sql`; the signed note page is `Patients/Notes/View.cshtml`. The desktop app syncs drafts, not signed notes: its offline chart would need the signed notes first.

### Phase 3: connect to other systems

**Step 11. Read-only FHIR R4 API**
- **Resources:** Patient, AllergyIntolerance, MedicationStatement, Condition, Observation (results and vitals) and Appointment, using the codes from step 2a.
- **Access:** staff only, through the existing sign-in and authorization. Every read goes through the PHI reader and is audited, RLS applies, and sensitive items follow the same rules as everywhere else.
- **Testing and dependencies:** responses are validated against the FHIR specification in tests. Prefer small, well-known open-source libraries, and note any you add in the decision log.
- **In the repo:** the entities are already shaped on FHIR R4 with its status codes. APIs live under `src/Ehr.Web/Api/` with a policy per group (`Program.cs`, `EhrPolicies`); a new group is an authorization change, which needs line-by-line review.

**Step 12. Simulated lab**
- A small service or command that receives orders and returns synthetic results through an authenticated inbound endpoint.
- Values come from step 2a's reference ranges and fit each patient's conditions, with delays and occasional abnormal values.
- It replaces seeded-only results for new orders, and is audited.
- **In the repo:** the cloud audit shipper (`Api/Audit/CloudAuditApi.cs`, a shared key) is the existing example of a service-to-service inbound endpoint; the local stack is `infra/compose/compose.yaml`.

### Phase 4: portal extras

All of these use the portal's sizes, work for proxies (step 6) under the sensitive-item rules, and put no health details in emails.
- **13a. Visit summaries,** written for patients from signed notes, with no internal codes.
- **13b. Download my record,** as a FHIR bundle (step 11) and as a printable page.
- **13c. Who viewed my record,** from the audit log: staff name and role, when, and which part, with emergency access marked and proxies included.
- **13d. Refill requests.** A patient asks to renew an active medication, which creates an inbox item. The clinician approves it, creating an order through the step 1 and step 8 checks, or declines it with a message.
- **13e. Pre-visit forms:**
  - questionnaires tied to appointments (step 4), whose answers appear in the chart for that visit;
  - PHQ-9 and GAD-7 built in, scored automatically, with the score and its severity band in the chart, and trended with step 9's charts;
  - their answers are sensitive items.

**Affects:** UC 5, UC 6.

**In the repo:** patients can't read `ehr.audit_events` (admins only, `audit_events_admin_select`) or `ehr.clinical_notes`, so 13a and 13c each need a new, narrow way for a patient to read their own (a policy or a `SECURITY DEFINER` function: `db/policies`). The portal has no print styles yet.

### Phase 5: dictation and AI

**Step 14. SOAP notes and visit-type templates**
- **The note format:** Subjective, Objective, Assessment and Plan, plus templates per visit type. Objective can include step 9's vitals.
- **What changes:** the structuring prompt, validation, the review page, the editor, signing and desktop drafts.
- **The eval:** regenerate the committed dictation eval results and keep their CI check green.
- **Touches:** the dictation prompt and note schema. **Affects:** UC 9, UC 10, UC 13, UC 14.
- **In the repo:** notes are History, Assessment and Plan today (`src/Ehr.Domain/Notes/NoteContent.cs`, columns on `clinical_drafts` and `clinical_notes`, `src/Ehr.Dictation/schema/note-fields.schema.json` and `structuring-prompt.md`, the sidecar, `eval/`, `eval/results/latest.json`, the desktop's drafts, whose plain-text Body contract UC 14 kept). Regenerating the eval needs the models running on this machine or the server; the accepted baseline is recorded on the server machine. If that can't be done here, stop and ask.

**Step 15. Dictation proposals beyond the note.** Ask Tahseen before starting: it changes FR-14.
- The proposed design: dictation may propose orders or allergy changes only as drafts that a clinician reviews and signs through the normal step 1, 2 and 8 flows.
- Nothing is placed automatically, and the same grounding checks as for note fields apply.
- **In the repo:** `tests/Ehr.Architecture.Tests/DictationOrdersTests.cs` and the eval gate (`test_an_order_or_allergy_change_shown_fails`) enforce FR-14 today.

**Step 16. Ambient listening.** Ask Tahseen before adding any model.
- **Capture:** conversations are recorded only after a consent prompt, with speakers separated.
- **The note** is drafted into the same review flow as dictation.
- **Testing** uses only synthetic voices generated locally. No recording is committed without Tahseen's say-so.

**Step 17. Languages beyond English.** Ask Tahseen before starting: it changes NFR-14, and it needs a new speech model, whose license has to fit NFR-15.

### Phase 6: desktop, security and operations

**Step 18. Session timeouts and emergency-access review**
- An idle timeout with a warning, for staff and for the portal.
- An admin workflow to mark each emergency access as reviewed, with a note, through the inbox.
- It can run any time after step 3.
- **In the repo:** cookie lifetimes are set in `src/Ehr.Domain/Accounts/AccountServiceCollectionExtensions.cs`; emergency access is `ehr.break_glass_accesses` and the flagged rows at `/admin/audit` (`Admin/Audit.cshtml`), with no reviewed state. Admins have no access to patient data: the review shows the patient by ID, as the audit page does.

**Step 19. Passkeys**
- Passkeys as a second factor, using the framework's built-in support if this .NET version has it. Recovery paths don't change.
- **Touches:** authentication.
- **In the repo:** .NET 10 (`global.json`), ASP.NET Core Identity 10.0.13. Confirm Identity's passkey support and its schema needs when the step starts.

**Step 20. Desktop distribution**
- **Auto-update** with Tauri's updater. Its signing key is never committed: ask Tahseen where it's kept.
- **Remote wipe:** revoking a desktop session makes the app delete its local data and key the next time it connects.
- **OS code signing:** ask Tahseen first, because it costs money (NFR-16).
- **In the repo:** `ehr.desktop_sessions` and `src/Ehr.Domain/Accounts/DesktopSessions.cs`; the app's key and store are `src-tauri/src/key.rs` and `store.rs`. The bundle is still named "EHR" (`productName`) with identifier `com.example.ehr`; renaming either moves the app's data folder, so decide it here with Tahseen.

**Step 21. Real deployment and operations.** Ask Tahseen before creating anything: there are accounts to create, costs, and NFR-16's budget of roughly $50.
- Until he approves, prepare these only as code and docs:
  - deployment documentation;
  - monitoring and alerting with open-source tools;
  - an automated restore drill that proves point-in-time recovery works.
- **In the repo:** `infra/compose` (primary, replica, Barman with point-in-time restore, simulated WAN), `infra/tailscale` (empty), and CI's stack job, which already checks replication and backups.

## Requirements to propose

New FR IDs (FR-37 onward) and use cases (UC 15 onward), as each step defines them, so the course document can be updated later.

| ID | Step | Proposed requirement |
| --- | --- | --- |
| FR-37 | 1 | The system shall let clinicians discontinue a signed order with a reason, and change one by signing a replacement, without ever editing or deleting the signed order; it shall keep who, when and why. (Extends UC 1; test cases TC-UC1-07 to TC-UC1-12.) |
| FR-38 | 2 | The system shall let clinicians add and change a patient's allergies, medications and diagnoses, each picked from a standard code list, never deleting an entry and keeping the history of every change. (Extends UC 4; test cases TC-UC4-07 to TC-UC4-12.) |
| FR-39 | 2 | The system shall treat mental-health, substance-use and other designated diagnoses, and any item a clinician marks, as sensitive: left out of a chart until asked for, with each such view recorded. |

## Phase merges

For each phase:
- its tag and branch;
- a pull request title and description;
- the protected paths it touched;
- a summary of its tests.

## Decision log

One entry per decision: date, step, the decision, and why.

- **2026-11-18, step 0. The plan starts from main at `b4ad35b`,** which includes the brand work (pull request 56). `expansion/integration` was created from it. No expansion branch or tag existed before, and step 1 had not started.
- **2026-11-18, step 0, correction (step 1). A discontinued order is stored with FHIR's request status `revoked`** and shown as "Discontinued". `Order.Status` holds FHIR R4 request status codes behind a check constraint (`OrderStatus.All`); FHIR has no `discontinued` code, and `revoked` is its code for a stopped request. "Resulted" (step 7) is `completed` for the same reason.
- **2026-11-18, step 0, correction (performance). Only the render benchmark is a CI gate today.** Page JavaScript size and first contentful paint are listed in AGENTS.md as gates to add "as the code they check appears"; nothing in `ci.yml` checks them. Each step measures both and records them in the log. Adding the two checks to CI is a follow-up for Tahseen to place: it isn't one of the plan's steps.
- **2026-11-18, step 0, correction (sensitive items). `docs/brand/README.md` says nothing about sensitive items.** Step 2 writes that section, in the guide's style, before building the screens, and the later steps follow it.
- **2026-11-18, step 0, correction (allergy codes). The allergy table was built for SNOMED CT codes,** and its "No known allergies" row uses SNOMED CT 716186003. The plan allows no SNOMED CT without approval, so step 2 codes allergens with RxNorm and UNII and replaces the "No known allergies" code with a marker of the app's own. No other SNOMED CT content is in the repo.
- **2026-11-18, step 0, correction (vitals). Blood pressure and heart rate are columns of `ehr.encounters`,** not observations. Step 9 moves them, and until then steps that show vitals read the visit's columns.
- **2026-11-18, step 0, correction (docs and tests). Not every use case has a `docs/UCnn.md` or a `TestCases/UCnn/` folder.** Docs exist for UC 1, 2, 6, 9, 10, 12, 13 and 14; test-case folders for UC 1 to 6, 11 and 12. A step that changes a use case without one creates it for what the step adds, and doesn't backfill the rest.
- **2026-11-18, step 0, note (requirement IDs). The plan's FR and NFR numbers are the ones the repo's test cases and docs use** (FR-05 orders, FR-14 dictation never orders, FR-16 to FR-19 the portal). They were matched to the course document in pull requests 52 to 55.
- **2026-11-18, step 0. Which CI run a step records.** The status table's record is itself a commit, so its own run can't be in it. A step records the run of its last commit before the record; the record commit changes only this file. After the merge, the run on `expansion/integration` is checked too, and a failure there is fixed before the next step starts.
- **2026-11-18, step 0, note (README). The README's "Not built yet" paragraph is out of date** (it still lists dictation's screens). Step 1 corrects it while updating the orders paragraph.

- **2026-11-18, step 1. Discontinuing is a column-level UPDATE grant with a trigger, not a `SECURITY DEFINER` function.** `ehr_app` may update `status`, `discontinue_reason` and `replaced_by_order_id` only; `orders_panel_update` limits rows to patients on the clinician's active panel; `ehr.discontinue_order()` runs as the caller and holds the update to active → revoked, once, with a reason, setting who and when. Nothing runs with more privilege than the caller, which is the narrowest that works. `RuntimeRoleTests` now lists every column grant (`ExpectedColumnPrivileges`) and fails on any other, because `has_table_privilege` doesn't see them.
- **2026-11-18, step 1. A change is enforced as a whole at commit.** A deferred constraint trigger refuses a replacement whose old order isn't discontinued and naming it; the discontinue trigger accepts "replaced by" only from the order that names this one, signed by the same clinician. A change keeps the order's type.
- **2026-11-18, step 1. Any clinician with the patient on their panel may discontinue or change a colleague's order,** and is the one recorded. That is the existing rule for who may order for a patient; limiting it to the signer would leave orders nobody on duty can stop.
- **2026-11-18, step 1. The reason for a change is optional;** without one the old order's reason reads "Replaced by a changed order". The reason for discontinuing is required, in the page and in the database.
- **2026-11-18, step 1. The chart links to discontinued orders and doesn't load or count them,** so the chart's render time is unchanged. The off-site sentence for the two actions is its own ("Changing or discontinuing an order needs the clinic's server."), beside the existing one that TC-UC1-04 and the brand guide quote.
- **2026-11-18, step 1. Orders aren't in the desktop app's offline copy** (they weren't before), so the desktop app didn't change. Whether offline charts should list orders is open for UC 7's owner.
- **2026-11-18, step 1. An independent review agent reads each step's protected-path changes before the merge.** It isn't the human line-by-line review AGENTS.md requires, which still happens on the phase pull request.

- **2026-11-18, step 2, design (decided before building; change it only with a new entry).**
  - *Reference codes.* One table, `ehr.clinical_codes` (kind: condition, medication or allergen; system: `icd-10-cm`, `rxnorm` or `unii`; code; display; category for allergens; sensitive). Not PHI: grants only. Its rows are generated SQL in `db/reference/`, written by `dotnet run tools/reference/import.cs` from pinned source files (checksums in the script; the files are downloaded to a folder outside the repo, never fetched at runtime). The migration job, the tests' database and the benchmark apply `db/reference/*.sql` after `db/policies`. Step 2 loads a curated subset (the codes the synthetic clinic uses and common primary-care ones); step 2a widens the filter to the full lists and adds the search index that needs.
  - *Sources used in step 2, none needing an account:* RxNorm Current Prescribable Content of 2026-11-02 (SCD for medications, IN for drug allergens), ICD-10-CM FY2027 code descriptions, FDA UNII (non-drug allergens). Attribution goes in `docs/expansion/reference-data.md`, started in this step.
  - *Writes.* `ehr_app` gets INSERT and UPDATE on `allergy_intolerances`, `medication_statements` and `conditions`, never DELETE. RLS: a clinician, for a patient on their active panel (the orders rule). A trigger on each refuses a change of the row's patient or ID.
  - *History.* One PHI table, `ehr.chart_history`, written only by a `SECURITY DEFINER` trigger on the three tables (who, when, the row before and after). Clinicians read it; nothing at runtime may insert, update or delete it. The write-audit trigger still records every change for admins.
  - *Sensitive rule.* A diagnosis is sensitive when its ICD-10-CM code is in F01–F99 (mental, behavioral and neurodevelopmental disorders, which include substance use F10–F19), A50–A64 (sexually transmitted infections), B20 and Z21 (HIV), O04 and Z33.2 (termination of pregnancy), T74 and T76 (abuse and neglect), X71–X83 and R45.851 (self-harm and suicidal ideation), or when a clinician marks it. A medication is sensitive when a clinician marks it (a rule by drug class waits for step 2a's class data). The database sets the flag from the code, in a trigger, so the app can't clear it for a code in a sensitive range.
  - *Showing sensitive items.* The server leaves them out of the chart unless the clinician asks ("Show sensitive items", a link that reloads the chart). Asking is recorded as its own audit action, `read_sensitive`. They never appear in the patient banner. `docs/brand/README.md` gets a "Sensitive items" section.
  - *Notes marked sensitive* (named in "Every step must meet these") are built with this step if it stays inside the budget, and otherwise recorded as a follow-up for Tahseen to place: the note pipeline is UC 13 and UC 14's.
- **2026-11-18, step 2. A view that shows sensitive items is one audit event, `read_sensitive`, in place of `read`,** not one of each. Two events doubled the cloud app's queue write and took the off-site chart over 20 ms with the commit included; one event says the same thing.
- **2026-11-18, step 2. An entry that is sensitive now is sensitive throughout its history.** The history page leaves out every change to it, including those made before it was marked. (The first build judged each change by the row as it was then; the review found the leak.)
- **2026-11-18, step 2. Existing diagnoses are marked on every pass of the migration job** (`65-chart-lists.sql`), not once in a migration: so widening the rule later reaches the rows already there. It lifts forced row-level security for that one statement, inside a transaction, as the seed does.
- **2026-11-18, step 2. The desktop app's offline copy never holds sensitive items.** It is told how many it wasn't given, and says so. A laptop is the likeliest place for a chart to be seen by the wrong person.
- **2026-11-18, step 2. New entries must be coded; old ones keep their names.** The form refuses a name that isn't a reference code, but an entry recorded before the codes keeps its name and its lack of a code until someone changes the name. The seed's own entries are still uncoded; step 2a's coherent seed codes them.
- **2026-11-18, step 2. The code search is a page of the clinical area (`/codes`),** not a new API group, so the area's existing policy covers it and no authorization code was added. Suggestions use the browser's own list (`datalist`), filled by a 1 KB script: no listbox widget to build or maintain.
- **2026-11-18, step 2. Admins see the values of sensitive entries in the write audit's details** at `/admin/audit`, as they see any changed value there. That page is the one place admins see patient data, by the team's earlier decision; it isn't a `read_sensitive`. Tahseen may want to revisit it.
- **2026-11-18, step 2. Not built, for Tahseen to place:** marking a *note* sensitive (the note pipeline is UC 13 and UC 14's; it fits step 10, addenda, or step 6, when proxies first need it); a button to record "No known allergies"; and three ranges the review suggested for the sensitive rule (T14.91 suicide attempt, self-harm poisoning codes in T36–T65, Z91.51 history of self-harm).

## Log

One entry per session or step: date, step, what was done, measurements (p99, page JavaScript size, first contentful paint), follow-ups, and where to resume.

- **2026-11-18, step 0.** Created `expansion/integration` from main (`b4ad35b`) and `expansion/step-00-plan` from it. Saved this plan and checked it against the repo: added "In the repo" to each step and the corrections above. No code changed, so there are no measurements. CI green on the plan commit; merged into `expansion/integration` and tagged `expansion-step-00`. Resume at step 1.
- **2026-11-18, step 1. Change and discontinue orders (UC 1, proposed FR-37).** Migration `OrderChanges` (five columns on `ehr.orders`, three check constraints); `db/policies` 10, 30 and 60; `ClinicalOrders.DiscontinueAsync` and a change through `SignAsync`; pages `Orders/Discontinue` and `Orders/Discontinued`, the order form's change mode, the chart's "Change" and "Discontinue…" links; new component `sc-list__actions` (noted in `docs/brand/README.md`). Tests: 14 in `OrderChangeSecurityTests`, TC-UC1-07 to TC-UC1-12 (the last a keyboard-only browser test), four more in `OrderTests`; 621 tests, 607 passed, 14 skipped (the performance cases, which need a benchmark run). `docs/UC1.md` and the README updated. Looked at every new and changed screen in light and dark, and at 640px; compared with the kit's notices, fields, table and buttons.
  - *Review.* The review agent could not break the database rules. Its fixes are in: the deferred trigger is replaced inside one transaction; column grants are now checked; the chart's confirmation is verified; three more tests.
  - *Measurements* (this Mac, 50,000 patients, durability on; p99 of the whole request / excluding the commit, ms). On-site: discontinued orders 8.0 / 7.1, change form 5.0 / 4.1, discontinue form 3.6 / 3.1; chart 7.8 / 7.1 and order form 3.0 / 2.7, as before. Off-site: 14.4 / 8.6, 17.3 / 12.5, 13.1 / 8.6. `--assert` passed. Page JavaScript as served, compressed: chart, change form and discontinue form 1,980 bytes, discontinued orders 1,058 (no script added or changed). First contentful paint, cold cache, median of 7 on the local stack: 16 ms on loopback, about 120 ms with 40 ms latency and 10 Mbit/s.
  - *Heads-up:* none beyond UC 1 (Tahseen).
  - *Follow-ups:* the chart's confirmation after discontinuing doesn't name the order; the race paths in `ClinicalOrders` (insert succeeds, update refused) are proven by the review's probes and the security tests but have no committed web test, because a stale page is refused earlier; the benchmark's table now has room for the longer row names.
  - *For step 2 and 2a,* license research is done (not committed): RxNorm's prescribable content, ICD-10-CM, UNII, UCUM and CVX need no account. **LOINC needs a free loinc.org account to download, MED-RT's terms are unclear, and no interaction list with a clearly free license was found: ask Tahseen before step 2a loads any of the three.** Step 2 needs none of them.
  - *Resume at step 2.*
- **2026-11-18, step 2. Editable allergies, medications and diagnoses, with standard codes (UC 4, proposed FR-38 and FR-39).** Migration `ChartLists` (tables `clinical_codes` and `chart_history`; `sensitive` on conditions and medications; `severity` and `code_system` on allergies; the SNOMED CT "No known allergies" code replaced). `db/policies` 10, 30, 40 and the new 65. `tools/reference/import.cs` and `db/reference/10-clinical-codes.sql`: 1,177 codes from RxNorm (2026-11-02), ICD-10-CM (FY2027) and UNII (2026-08-04), applied by the migration job, the tests and the benchmark; `docs/expansion/reference-data.md`. One entry form for the three lists with a code search (`/codes`, `js/code-search.js`), a history page, the sensitive-items rule on the chart, history and offline copy, allergies in the banner on the note and dictation pages, and a "Sensitive items" section in the brand guide. Tests: 677, 659 passed, 18 skipped (performance cases); 12 new security tests, TC-UC4-07 to TC-UC4-12 (the last a keyboard-only browser test), five more in `ChartListTests`. `cargo fmt`, `clippy` and the desktop tests pass. Looked at every new and changed screen in light and dark, and at 640px.
  - *Review.* The review agent failed the first build on two leaks of sensitive items (the history page, and unmarked existing rows after an upgrade). Both are fixed, with tests; its smaller points are in too.
  - *Measurements* (this Mac, 50,000 patients, durability on; p99 whole request / excluding the commit, ms). On-site: chart 7.2 / 6.4, chart with sensitive items 6.4 / 6.0, entry form 3.7 / 3.3, code search 1.5 / 1.4, history 2.6 / 2.3. Off-site: chart 12.1 / 7.7, entry form 12.0 / 7.5, code search 3.0 / 2.9, history 13.9 / 7.5; the chart with sensitive items was 24.1 / 15.2 with two audit events and was not measured again after going to one (CI's run passed). `--assert` passed. Page JavaScript as served, compressed: entry form 1,634 bytes (the new `code-search.js` is about 0.6 KB of it), chart 1,980, history 1,058. First contentful paint, cold cache, median of 7: 16 to 20 ms on loopback, about 116 ms with 40 ms latency and 10 Mbit/s. Seeding 50,000 patients is unchanged in size; its time wasn't recorded (step 2a records it).
  - *Heads-up:* Diego (UC 4: the chart gained edit links, a notice and a history link), Sara (UC 7: the offline chart contract gained `sensitiveNotKept`), Samir and Satwik (the banner on dictation and note pages now shows allergies).
  - *Follow-ups:* the items in the decision log's "Not built" entry; the code search needs an index before step 2a loads the full lists (about 100,000 rows); the seed's entries are uncoded until 2a.
  - *Resume at step 2a.* **Before it loads LOINC, drug classes or an interaction list, Tahseen has to decide** (see the step 1 log entry and `docs/expansion/reference-data.md`). RxNorm, ICD-10-CM, UNII, UCUM and CVX can be loaded in full without him.
