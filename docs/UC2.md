# UC 2 — Read records during an on-site outage

SwiftChart EHR · Tahseen Arnan · CS 3354 Software Engineering, Fall 2026

## What it does

A clinician already signed in, off-site, keeps reading charts, search results and their panel while the clinic's on-site server can't be reached. The cloud app serves those pages from its replica as `ehr_read`, records each read in its own audit queue (a file flushed to disk before the page is sent), and every page says the system is read-only. A write is refused with an explanation, and what the clinician typed is kept. When the on-site server is back, the queued reads ship to it within a couple of seconds and writing works again. If the clinic's data has to be recovered, Barman restores the primary to a point in time.

Most of this was built with the cloud milestone (forwarding, replica reads, the read-only notice, the "Not saved" page, the audit queue and recovery). This change measures off-site render time (NFR-03), keeps typed text on the "Not saved" page, stops pages offering changes that can't be saved, and adds tests for the notes, draft sync and dictation that landed since.

## Course of events, code and tests

The tests are in `tests/Ehr.Web.Tests` unless named otherwise. `CloudCutOff` is the cloud app with its link to the on-site server down; `CloudPrimaryDrill` is the on-site server up but its database down (`WebApps.cs`).

| Step | What happens | Code | Tests |
|---|---|---|---|
| 1. Off-site, signed in, the clinician opens search, the panel or a chart | Served from the replica as `ehr_read`; reads aren't forwarded | `Program.cs` (cloud registrations), `Forwarding/CloudForwardingMiddleware.cs`, `Ehr.Domain/Phi/PhiReader.cs` | `PatientSearchTests.Off_site_during_an_outage_search_and_as_you_type_come_from_the_replica_and_their_reads_are_queued_then_shipped`, `PatientChartTests.Off_site_during_an_outage_the_chart_comes_from_the_replica_and_its_one_read_is_queued_then_shipped`, `OffSiteOutageTests.The_panel_is_read_from_the_replica_and_each_patient_listed_is_audited`, `CloudForwardingTests.With_the_on_prem_app_unreachable_reads_come_from_the_replica`, `CloudForwardingTests.During_an_outage_reads_after_a_save_come_from_the_replica` |
| 2. Each read is audited | One event per patient read, appended to the cloud app's queue | `Ehr.Domain/Audit/AuditRecorder.cs`, `CloudAudit/CloudAuditQueue.cs`, `CloudAudit/CloudAuditEvents.cs` | `CloudAuditTests.With_the_primary_database_down_off_site_pages_say_read_only_and_reads_stay_queued`, `CloudAuditTests.An_off_site_search_queues_one_read_per_patient_listed_and_ships_them_from_the_cloud`, `CloudAuditQueueTests` |
| 3. The site says it is read-only | The shipper's check every 2 s finds the on-site server can't save; every page shows the notice | `Forwarding/OnPremStatus.cs`, `CloudAudit/CloudAuditShipper.cs`, `Pages/Shared/_Layout.cshtml` | `CloudAuditTests.During_an_outage_an_answer_the_on_prem_app_still_gives_leaves_the_site_read_only`, `CloudAuditTests.A_save_answered_with_a_server_error_leaves_the_read_only_notice_as_it_was` |
| 4. A write is refused with an explanation, and what was typed is kept | 503 and the "Not saved" page, which shows the typed text again; htmx and the page scripts keep the form; read-only pages offer no change | `CloudForwardingMiddleware.cs`, `Forwarding/ShownIfNotSavedAttribute.cs`, `Ehr.Design/wwwroot/js/request-errors.js`, `note-editor.js`, `dictation.js`; the pages in the table below | `CloudForwardingTests.With_the_on_prem_app_unreachable_a_save_fails_clearly`, `CloudForwardingTests.A_form_post_that_cannot_be_forwarded_gets_a_page_that_explains`, `CloudForwardingTests.An_htmx_post_that_cannot_be_forwarded_still_gets_the_short_message`, `OffSiteOutageTests` (the first five tests), `PatientPanelTests.With_the_on_prem_app_unreachable_the_chart_offers_no_changes`, `BreakGlassTests.With_the_on_prem_app_unreachable_emergency_access_is_not_offered`, `DictationPageTests.Off_site_with_the_clinics_server_down_no_dictation_is_offered` |
| 5. Signing out still works | The cloud app signs out itself and queues the event | `Forwarding/HandledOffSiteAttribute.cs`, `Areas/Clinical/Pages/Account/SignOut.cshtml.cs` | `StaffSignInTests.Off_site_signing_out_works_while_the_on_prem_app_cant_save`, `CloudForwardingTests.Signing_out_off_site_is_done_by_the_cloud_app_and_recorded_from_there` |
| 6. The on-site server is back | The next check ships the queued reads (as source `cloud`), the notice goes, and saves work again | `CloudAuditShipper.cs`, `Api/Audit/CloudAuditApi.cs` | `CloudAuditTests.The_read_only_notice_goes_once_the_on_prem_app_is_back_and_the_reads_arrive`, `OffSiteOutageTests.Once_the_clinics_server_is_back_queued_reads_arrive_and_saves_work_again` |
| Alternative: restore | Barman restores the primary to a point in time | `infra/barman`, `infra/compose` ("Backup and point-in-time restore") | The drill below |
| NFR-03: off-site render time | p99 server time under 20 ms, excluding the commit, at 50,000 patients | `bench/Ehr.Bench/RenderBenchmark.cs` (`OffSiteApp`, `TimedAuditQueue`), CI's benchmark job | `BenchmarkTests.Off_site_the_audit_queues_append_counts_as_the_commit`; the run below |
| UC 14, UC 9 during an outage | Notes and the dictation page are read from the replica and audited; signing, new notes and dictation are refused | `Notes/*.cshtml`, `Dictate.cshtml`, `Api/Dictation` | `OffSiteOutageTests.Notes_are_read_from_the_replica_and_audited_and_note_writes_are_refused`, `OffSiteOutageTests.The_dictation_page_read_is_audited_and_dictation_is_refused`, `ClinicalNoteTests.Primary_outage_refuses_signature_and_keeps_the_server_draft_available`, `DictationApiTests.Off_site_with_the_link_down_dictation_says_the_server_cannot_be_reached` |
| UC 8 (desktop) during an outage | Draft push and pull, and the offline panel, are refused (503); quick text sync still reads the replica | `Api/Desktop/DraftSyncApi.cs`, `Api/Desktop/DesktopApi.cs` | `OffSiteOutageTests.The_desktop_apps_draft_sync_and_offline_panel_are_refused_and_quick_text_sync_still_reads` |
| Signing in during an outage | Not possible; see Limits | `CloudForwardingMiddleware.cs` | `OffSiteOutageTests.During_an_outage_nobody_can_sign_in_off_site` |

## Every write form, when the on-site server goes down between loading the page and sending it

| Form | With JavaScript | Without JavaScript | Loaded while read-only |
|---|---|---|---|
| Chart: Add to / Remove from my patients | "Not saved" page; nothing was typed | Same | Not offered |
| Chart: Issue an enrollment code | "Not saved" page; nothing was typed | Same | Not offered |
| Chart: New note | "Not saved" page; nothing was typed | Same | Not offered |
| Emergency access reason | The page uses a plain form either way: "Not saved" page with the reason shown again | Same | Not offered (says why) |
| Note editor: Save draft, autosave, Sign note… | The text stays on screen with "could not confirm this save" | "Not saved" page with History, Assessment and Plan shown again | **New:** the draft as last saved, no editor |
| Note confirmation: Confirm and sign | The page stays, "Signature not confirmed" | "Not saved" page; only a checkbox was set | **New:** no signature offered |
| Dictation | Recording carries on; the audio waits in the page and is sent again when the connection returns | Needs JavaScript; not offered | Not offered |
| Quick text: Add, Save, Keep mine | htmx leaves the form as typed and says "Not saved" | "Not saved" page with Shortcut and Body shown again | **New:** no add form or Edit buttons |
| Sign-in steps (password, code, enrollment, new password, setup) | "Not saved" page; nothing typed is shown back | Same | Still offered (sign-in unchanged) |
| Sign out | Works: the cloud app does it and queues the event | Same | Offered |

"Shown again" means a read-only box to copy from, on the "Not saved" page, only for a form the browser sent from this site (`Sec-Fetch-Site: same-origin`), never a password or code, and sent with `no-store`.

## Off-site render time (NFR-03)

The benchmark (`bench/Ehr.Bench`, see "Synthetic data and the benchmark" in the README) now makes the same requests off-site after the on-site ones: to the cloud app reading as `ehr_read`, with its link to the on-site server down, as in this use case. The cloud app records reads in its audit queue rather than the database, so in the off-site rows the audit insert is 0 and the queue's write and flush to disk count as the commit: that is where the page waits for the disk, as an on-site page does at its commit. `--assert` gates the off-site rows as it does the on-site ones: p99 excluding the commit under 20 ms.

**Pending.** The results, and whether the off-site rows stay gated, wait on a run on a quiet machine. The first full run at 50,000 patients passed on-site but failed six off-site rows, only in their tails (p50s of 2 to 5 ms); it ran while other builds, tests and benchmarks were running on the same machine, so it isn't a reading. The owner decides what to do if a clean run fails the same way.

## Restore drill

Run on 2026-11-15 (US Central; 2026-11-16 UTC) on the local Compose stack, with the web and migration images rebuilt from `main` at 86dc093 (12 migrations applied, `ClinicalNotes` among them), following "Backup and point-in-time restore" in `infra/compose/README.md`. `barman check onprem` passed every check first.

- **Target time:** 2026-11-16 03:28:39.439106+00.
- **The change to undo:** a scratch table, `public.restore_drill`, held 5 rows at the target time; 5 more were inserted after it, so the primary had 10.
- **Restore:** the WAL in progress was shipped with `barman switch-wal --force --archive onprem`; `barman recover onprem latest /var/lib/barman/restore --target-time "<target>"` completed; `restore-db` started in recovery, paused, with its last replayed transaction at 03:28:37.158222+00.
- **Cleanup:** as the README says (`restore-db` removed, the restore folder deleted), and the scratch table dropped.

| Table | Primary at the target time | Restored copy |
|---|---:|---:|
| `patients` | 50,000 | 50,000 |
| `audit_events` | 92 | 92 |
| `clinical_notes` | 0 | 0 |
| `clinical_drafts` | 0 | 0 |
| `staff_users` | 3 | 3 |
| `patient_users` | 2 | 2 |
| `patient_assignments` | 2 | 2 |
| `__ef_migrations_history` | 12 | 12 |
| `restore_drill` | 5 (10 after the change) | 5 |

Every count matched. The drill's limit: `clinical_notes` and `clinical_drafts`, added since the cloud milestone, were empty on this stack, so it shows that their schema restores, not their rows; orders (UC 1) weren't on `main` yet.

## Limits

- **Signing in during an outage isn't possible.** A clinician who isn't signed in gets the sign-in page from the replica, but the sign-in is a write (it checks and records against the primary), so it is forwarded and refused with the "Not saved" page. UC 2 assumes the clinician signed in before the outage. Sign-in is unchanged.
- **The desktop app's draft pull and offline panel are refused during an outage**, not read from the replica: they are POSTs, which the cloud app forwards (UC 8's design). The desktop app keeps showing its encrypted local copy. Whether they should read the replica is an open question for the owner.
- The typed text is shown again only by browsers that send `Sec-Fetch-Site` (all current ones); an older browser gets the "Not saved" page with the Back link, as before.
- The browser behaviour (Back, the scripts keeping text) was checked through the web tests' HTTP requests and by reading the scripts, not in a real browser.
- The off-site benchmark rows run the cloud app against the same PostgreSQL, which stands in for the replica as in the web tests, with no network; `--stack` measures on-site only, since it has no `ehr_read` login.
- The patient portal and the admin pages were not reviewed for UC 2, which is a clinician's use case.
