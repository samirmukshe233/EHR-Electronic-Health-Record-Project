# TC-UC11-05 — Sign-in data is encrypted in transit and at rest (manual part)

The manual part of TC-UC11-05: what needs the running stack. The automated part (the session cookie's flags, the
password hash and the encrypted cookie keys) is `TC_UC11_05_Nfr06_Encryption.cs` in this folder.

| Field | Value |
|---|---|
| Test Case ID | TC-UC11-05 (manual part) |
| Purpose / scenario | Passwords, codes and sessions only travel encrypted, and what sign-in stores is not readable from the disk: the sign-in page is served over HTTPS with a trusted certificate and never over plain HTTP, every network connection to both databases is TLS, the stored keys and passwords aren't readable, and the disk holding the database volumes is encrypted. |
| Type | Non-functional (security: encryption in transit and at rest) |
| Requirements | NFR-06, with FR-28 |
| Use case | UC 11, Sign in with multi-factor authentication |
| Preconditions | The local stack is running (`infra/compose/README.md`), with the hosts file entries and the mkcert certificate from its one-time setup, and the example accounts exist (`dotnet run infra/compose/test-accounts.cs -- setup`). For step 6, an administrator PowerShell on Windows. |
| Inputs and system conditions | The off-site address `https://ehr-remote.example.com:9443` in a browser and with `curl.exe`; `psql` as the database superuser through `docker compose exec`, run from `infra/compose`. The clinic's server is up. |
| Test data | The example clinician, `clinician@example.com`. |
| Expected result | Step 1: a valid certificate for `*.example.com`. Step 2: no sign-in page over plain HTTP (an error, or the connection closed). Step 3: `ssl` is `t` on every row, for both databases. Step 4: every password starts `AQAAAA`. Step 5: every key holds `encryptedSecret`. Step 6: `Protection Status: Protection On` (on a Mac, `FileVault is On`). Step 7: no authenticator key is readable base32; see the known gap below. |
| Creator | Luke Garcia |

## Steps

1. Open `https://ehr-remote.example.com:9443/sign-in` and check the padlock and the certificate.
2. Request the same page over plain HTTP:
   `curl.exe -v --max-time 10 http://ehr-remote.example.com:9443/sign-in`
3. From `infra/compose`, list the database connections on each database:
   `docker compose exec -u postgres onprem-db psql -c "select a.usename, a.client_addr, s.ssl, s.version from pg_stat_ssl s join pg_stat_activity a using (pid) where a.client_addr is not null"`,
   then the same with `cloud-db`.
4. Read the stored passwords:
   `docker compose exec -u postgres onprem-db psql -d ehr -c "select email, left(password_hash, 8) from ehr.staff_users"`
5. Read the stored cookie keys:
   `docker compose exec -u postgres onprem-db psql -d ehr -c "select xml from ehr.data_protection_keys"`
6. Check the disk: `manage-bde -status C:` (BitLocker is not available on Windows Home; record that instead).
7. Read the stored authenticator keys:
   `docker compose exec -u postgres onprem-db psql -d ehr -c "select user_id, value from ehr.staff_user_tokens where name = 'AuthenticatorKey'"`

## Known gap

Step 7 is expected to fail today. ASP.NET Identity stores the authenticator key in `ehr.staff_user_tokens` as plain
base32 unless personal data protection is turned on, and the project relies on the disk's encryption for it
("Encryption" in `infra/compose/README.md`). The automated part keeps the matching check, skipped, until the team
decides.

## Results

Not run yet.

| Step | Date run | Result |
|---|---|---|
| 1. HTTPS with a trusted certificate | | |
| 2. No sign-in page over plain HTTP | | |
| 3. TLS on every database connection | | |
| 4. Passwords stored as hashes | | |
| 5. Cookie keys encrypted | | |
| 6. Disk encryption | | |
| 7. Authenticator keys not readable | | |
