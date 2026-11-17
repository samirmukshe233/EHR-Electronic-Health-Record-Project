# TC-UC5-05 — Portal sign-in works in three browser engines

A manual test case. It is run in the same browser run as TC-UC6-06
(`tests/Ehr.Web.Tests/TestCases/UC6/TC_UC6_06_Nfr11_ModernBrowsers.md`): that run signs the patient in before it
reads their results, and signs them out after. This file records the sign-in part of it.

| Field | Value |
|---|---|
| Test Case ID | TC-UC5-05 |
| Purpose / scenario | A patient with a confirmed portal account signs in and out of the portal in each of three browser engines (Chromium, Gecko and WebKit); sign-in behaves the same in each. |
| Type | Non-functional (NFR-11) |
| Requirements | NFR-11 |
| Use case | UC 5, Register for the patient portal |
| Preconditions | The local stack is running (`infra/compose/README.md`). A synthetic patient has a confirmed portal account. |
| Inputs and system conditions | The portal at its local HTTPS address, opened in each browser below. The clinic's server is up. |
| Test data | The synthetic patient of TC-UC6-06's run. Which patient was used is not stated in PR #43. |
| Expected result | In every browser: the sign-in form takes the address and password and lands on the patient's portal home; signing out returns to the sign-in page, and Back afterwards shows the sign-in page, not the patient's pages. |
| Creator | Simar Rekhi |

## Steps

These are steps 1, 5 and 6 of TC-UC6-06's run.

1. Open the portal and sign in as the patient (TC-UC6-06, step 1).
2. Sign out (TC-UC6-06, step 5).
3. Press Back (TC-UC6-06, step 6).
4. Repeat in each browser below.

## Results

The same run as TC-UC6-06, as reported by Simar Rekhi in PR #43, on the local stack with Playwright 1.49. Not re-run in
this change.

| Browser | Engine | Version | Result |
|---|---|---|---|
| Chrome | Chromium | 157 | Pass, reported by Simar Rekhi in PR #43 |
| Edge | Chromium | 157 | Pass, reported by Simar Rekhi in PR #43 |
| Firefox | Gecko | 132 (Playwright's build) | Pass, reported by Simar Rekhi in PR #43 |
| WebKit | WebKit | 18.2 (Playwright's build, not the Safari app) | Pass, reported by Simar Rekhi in PR #43 |
