# TC-UC4-06 — Records stay intact after an outage and a restore (manual part)

TC-UC4-06 needs the running server stack: a real outage of the on-site primary, Barman's backup and a point-in-time
restore. The test suite has none of these, so this test case is manual only. It has no automated part. The outage on
its own is checked automatically by `TC_UC2_06_Nfr09_OneAuthoritativeCopy.cs`: a write refused during an outage changes
nothing, and after recovery the save lands once, on the primary.

| Field | Value |
|---|---|
| Test Case ID | TC-UC4-06 (manual part) |
| Purpose / scenario | A clinician's charts are intact after an outage of the on-site server and a restore from backup. The restored copy holds exactly the rows the primary held at the target time, and a chart opened from it matches the chart opened before the outage. |
| Type | Non-functional (NFR-09) |
| Requirements | NFR-09, FR-35, FR-36 |
| Use case | UC 4, View a patient's chart |
| Preconditions | The local stack is running (`infra/compose/README.md`), the synthetic clinic is seeded ("Synthetic data and the benchmark" in the root README), and the example accounts exist (`dotnet run infra/compose/test-accounts.cs -- setup`). `barman check onprem` passes every check, so the first base backup has finished. |
| Inputs and system conditions | The on-site app at `https://ehr.example.com:8443`; `docker compose` in `infra/compose`; the restore steps in "Backup and point-in-time restore" in `infra/compose/README.md`. |
| Test data | Clinician `clinician@example.com`, with the example patient (MRN `SYN0000001`) on their panel. A scratch table, `public.restore_drill`, with 5 rows before the target time and 5 more after it. Everything is synthetic. |
| Expected result | Every table's row count in the restored copy equals the primary's at the target time, and `restore_drill` has 5 rows, not 10. The example patient's chart (identity, allergies, medications, diagnoses, visits) read from the restored copy is the same as the chart saved in step 2. No row was lost or duplicated by the outage. Acceptance criterion: zero differences in row counts and in the example patient's chart. |
| Creator | Diego Valenzuela |

## Steps

1. Sign in as the clinician (password, then the code from `dotnet run infra/compose/test-accounts.cs -- code`), and open the example patient's chart.
2. Save the chart's rows: `docker compose exec -T -u postgres onprem-db psql -d ehr -Atc "select p::text from ehr.patients p where mrn = 'SYN0000001'"`, and the same for `ehr.allergy_intolerances`, `ehr.medication_statements`, `ehr.conditions` and `ehr.encounters` with `where patient_id = (select id from ehr.patients where mrn = 'SYN0000001')`.
3. Create `public.restore_drill` and insert 5 rows. Note the target time: `docker compose exec -u postgres onprem-db psql -Atc "select now()"`.
4. Take the on-site server down: `docker compose stop onprem-db`. Off-site, at `https://ehr-remote.example.com:9443`, open the chart: it is served from the replica, read-only. Bring the server back: `docker compose start onprem-db`.
5. Insert 5 more rows into `public.restore_drill` (the change to undo).
6. Restore to the target time, as `infra/compose/README.md` says: `barman switch-wal --force --archive onprem`, then `barman recover onprem latest /var/lib/barman/restore --target-time "<target>"`, then `docker compose --profile restore up -d restore-db`.
7. In `restore-db`, count the rows of every table in schema `ehr` and of `public.restore_drill`, and compare them with the primary's counts at the target time.
8. In `restore-db`, run the queries of step 2 again and compare them with what step 2 saved.
9. Clean up: `docker compose --profile restore rm -sf restore-db`, delete the restore folder, and drop `public.restore_drill` on the primary.

## Results

| Date | Stack | Result |
|---|---|---|
| 2026-11-15 | Local Compose stack, `main` at 86dc093 | **Partial: steps 3, 5, 6, 7 and 9 only.** Every row count matched (`patients` 50,000 and 50,000, `restore_drill` 5 and 5, and every other table). That is the restore drill recorded in `docs/UC2.md` ("Restore drill"). The outage (step 4) and the chart comparison (steps 1, 2 and 8) were not part of it. |

**Status: Not Run** as a whole. The restore drill above is supporting evidence. Record the date, the commit and the
result of every step here when the full procedure is run.
