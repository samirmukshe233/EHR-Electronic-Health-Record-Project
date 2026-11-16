# TC-UC6-15 — The results page works in any modern browser

A manual test case: the repository has no browser automation, so this one is run by hand (or with a local Playwright
install, as below) and its results recorded here.

| Field | Value |
|---|---|
| Test Case ID | TC-UC6-15 |
| Purpose / scenario | A patient signs in, reads their test results, opens one, signs out and presses Back, in each modern browser; the page behaves the same in each. |
| Type | Non-functional |
| Requirements | NFR-11 |
| Use case | UC 6, View test results in the portal |
| Preconditions | The local stack is running (`infra/compose/README.md`). A synthetic patient has a confirmed portal account and released results, one of them above its normal range. |
| Inputs and system conditions | The portal at its local HTTPS address, opened in each browser below. The clinic's server is up. |
| Test data | A synthetic patient with a released result above its normal range of 4 to 5.6 %. Which patient and which test were used is not stated in PR #43. |
| Expected result | In every browser: the results page lists the results with the expand marker on each and the date labelled UTC; opening the result shows "Your result is above the normal range (4 to 5.6 %)."; after signing out, Back goes to the sign-in page, not to the results (`Cache-Control: no-store`). |
| Creator | Simar Rekhi |

## Steps

1. Open the portal and sign in as the patient.
2. Open **Results** (`/portal/results`).
3. Check that each result shows its expand marker and its date labelled UTC.
4. Open the result that is above its normal range and read its meaning.
5. Sign out.
6. Press Back.

## Results

As reported by Simar Rekhi in PR #43, run on the local stack with Playwright 1.49. Not re-run in this change.

| Browser | Version | Build | Date run | Result |
|---|---|---|---|---|
| Chrome | 157 | The vendor's build | not stated | Pass, reported by Simar Rekhi in PR #43 |
| Edge | 157 | The vendor's build | not stated | Pass, reported by Simar Rekhi in PR #43 |
| Firefox | 132 | Playwright's build | not stated | Pass, reported by Simar Rekhi in PR #43 |
| WebKit | 18.2 | Playwright's build (WebKit is Safari's engine, not the Safari app) | not stated | Pass, reported by Simar Rekhi in PR #43 |
