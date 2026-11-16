# TC-UC12-05 — Emergency access is encrypted in transit and at rest (manual part)

The manual part of TC-UC12-05: what needs the running stack. The automated part (the reason saved on-prem through the
forwarding link, the cloud read reaching the log, the chart never cached, the stack's TLS settings, and the cloud app
refusing a plain-HTTP on-prem address) is `TC_UC12_05_Nfr06_Encryption.cs` in this folder.

| Field | Value |
|---|---|
| Test Case ID | TC-UC12-05 (manual part) |
| Purpose / scenario | Emergency access from off-site travels only over TLS: the browser to the cloud app, the cloud app to the on-prem app, and both apps to their databases. The reason and the audit events it leaves are stored only on encrypted disks, and the chart isn't kept in the browser's cache. |
| Type | Non-functional (security: encryption in transit and at rest) |
| Requirements | NFR-06, with FR-06 and FR-32 |
| Use case | UC 12, Open a chart through emergency access |
| Preconditions | The local stack is running (`infra/compose/README.md`), with the hosts file and the mkcert certificate from its one-time setup, the synthetic clinic seeded, and the example accounts set up (`dotnet run infra/compose/test-accounts.cs -- setup`). `SYN0000004` is not on the example clinician's panel. Commands run from `infra/compose`; step 6 in an administrator PowerShell window. |
| Inputs and system conditions | The off-site address `https://ehr-remote.example.com:9443`, with the on-prem app reachable. |
| Test data | Clinician `clinician@example.com` / `local-clinician-test-1234`, with a code from `dotnet run test-accounts.cs -- code`. MRN `SYN0000004`. Reason: `Off-site consult for the on-call physician.` |
| Expected result | Step 1: a valid certificate for `*.example.com`. Step 2: the chart opens with the emergency notice, and the browser's developer tools show `Cache-Control: no-store` on it. Step 3: `ssl` is `t` on every row, for both databases. Step 4: the `break_glass` event with source `onprem`, and the chart's `read` with source `cloud`. Step 5: after signing out, Back shows the sign-in page, not the chart. Step 6: `Protection Status: Protection On` (on a Mac, `FileVault is On`). |
| Creator | Luke Garcia |

## Steps

1. Open `https://ehr-remote.example.com:9443` and sign in as the clinician with the code. Check the padlock: the
   certificate is valid and issued for `*.example.com`.
2. Search for MRN `SYN0000004`, open the chart, enter the reason and choose **Open the chart**. Open the developer
   tools (F12), Network tab, reload, and read the chart's `Cache-Control` response header. Note the patient's ID from
   the address bar.
3. Check the database connections:
   `docker compose exec -u postgres onprem-db psql -c "select a.usename, a.client_addr, s.ssl, s.version from pg_stat_ssl s join pg_stat_activity a using (pid) where a.client_addr is not null"`
   and the same with `cloud-db`.
4. Wait a few seconds for the cloud app to ship its audit events, then:
   `docker compose exec -u postgres onprem-db psql -d ehr -c "select action, table_name, source, reason from ehr.audit_events where patient_id = '<id>' order by id"`
5. Sign out, then press the browser's Back button.
6. Check the disk that holds Docker's volumes: `manage-bde -status C:` (BitLocker is not available on Windows Home;
   record that instead). On a Mac: `fdesetup status`.

## What is encrypted at rest, and how

The reason is stored twice, in `ehr.break_glass_accesses` and in the `break_glass` audit event, as plain columns.
PostgreSQL doesn't encrypt its files, and Barman doesn't encrypt its backups, so they are as encrypted as the disk they
live on ("Encryption" in `infra/compose/README.md`). Step 6 is that check; without disk encryption, this case fails.

## Results

Not run yet.

| Step | Date run | Result |
|---|---|---|
| 1. Certificate | | |
| 2. Chart not cached | | |
| 3. Database TLS | | |
| 4. Audit events and their sources | | |
| 5. Back after signing out | | |
| 6. Disk encryption | | |
