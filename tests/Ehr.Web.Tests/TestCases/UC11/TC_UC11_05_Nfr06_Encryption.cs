using System.Net;
using Ehr.Data.Accounts;
using Ehr.Testing;
using Npgsql;
using Xunit;
using static Ehr.Web.Tests.Browser;

namespace Ehr.Web.Tests.TestCases.UC11;

// Test Case ID: TC-UC11-05
// Purpose / scenario: What a sign-in sends and stores is protected. The session cookie is only sent over HTTPS, can't
//   be read by scripts, and isn't sent with other sites' requests; the password is stored as a hash; the keys that
//   protect cookies are encrypted before they are stored.
// Type: Non-functional (security: encryption in transit and at rest)
// Requirements: NFR-06, with FR-28
// Use case: UC 11, Sign in with multi-factor authentication
// Preconditions: A clinician account exists and has finished setup with an authenticator app. The apps have the Data
//   Protection certificate, as the stack's do.
// Inputs and system conditions: POST the email and password, then the code, on the cloud app (the off-site address),
//   reading the Set-Cookie header of the answer to the code; then, as the database superuser, SELECT the account's
//   password_hash from ehr.staff_users and every xml from ehr.data_protection_keys.
// Test data: Clinicians "Dr. Cookie" and "Dr. Hashed", with fresh @example.test addresses and the fixture's staff
//   password, "a-long-enough-password".
// Steps:
//   1. Sign in off-site with the password and code, and read the __Host-Ehr.Staff cookie the code's answer sets.
//   2. Sign in on-site, then read the account's stored password.
//   3. Read the stored Data Protection keys.
// Expected result: Step 1's cookie has the Secure, HttpOnly and SameSite=Strict flags. Step 2's column holds an ASP.NET
//   Identity hash (starting "AQAAAA"), never the password. Step 3 finds at least one key, and every key holds an
//   encryptedSecret. The parts that need the running stack (database connections, plain HTTP, the disk, and the
//   authenticator key) are the manual part, TC_UC11_05_Nfr06_Encryption.md in this folder.
// Creator: Luke Garcia
public class TC_UC11_05_Nfr06_Encryption(WebApps apps) : IClassFixture<WebApps>
{
    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_staff_session_cookie_is_secure_http_only_and_same_site_strict()
    {
        var clinician = await apps.CreateStaffAsync("Dr. Cookie", StaffRoles.Clinician);
        var jar = new CookieContainer();
        using var codePage = await PostFormAsync(Client(apps.Cloud, jar), "/sign-in", "/sign-in", ("Input.Email", clinician.Email), ("Input.Password", clinician.Password));

        using var signedIn = await PostFormAsync(Client(apps.Cloud, jar, followRedirects: false), "/sign-in/code", codePage, ("Code", Totp.Code(clinician.AuthenticatorKey)));

        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        var cookie = Assert.Single(signedIn.Headers.GetValues("Set-Cookie"), header => header.StartsWith("__Host-Ehr.Staff=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Passwords_are_stored_hashed_and_cookie_keys_encrypted()
    {
        var clinician = await apps.CreateStaffAsync("Dr. Hashed", StaffRoles.Clinician);
        await SignInAsync(apps.OnPrem, clinician);

        await using var db = await apps.OpenDatabaseAsync();
        await using (var command = new NpgsqlCommand("SELECT password_hash FROM ehr.staff_users WHERE id = @id", db))
        {
            command.Parameters.AddWithValue("id", clinician.Id);
            var hash = (string)(await command.ExecuteScalarAsync(Cancel))!;
            Assert.DoesNotContain(clinician.Password, hash);
            // ASP.NET Identity's v3 format: PBKDF2, with the version, settings and salt in front.
            Assert.StartsWith("AQAAAA", hash);
        }

        await using (var command = new NpgsqlCommand("SELECT xml FROM ehr.data_protection_keys", db))
        await using (var reader = await command.ExecuteReaderAsync(Cancel))
        {
            var keys = 0;
            while (await reader.ReadAsync(Cancel))
            {
                keys++;
                Assert.Contains("encryptedSecret", reader.GetString(0));
            }

            Assert.True(keys > 0, "No Data Protection key was stored.");
        }
    }

    // A known gap, kept here so it isn't lost: ASP.NET Identity stores the authenticator key (the secret behind every
    // code) in ehr.staff_user_tokens as plain base32 unless personal data protection is turned on, and the project
    // relies on the disk's encryption for it instead ("Encryption" in infra/compose/README.md). Remove Skip once the
    // key is protected, or delete this test if the team accepts disk encryption as the control.
    [Fact(Skip = "Known gap: the authenticator key is stored readable in ehr.staff_user_tokens (see the comment above).")]
    public async Task The_authenticator_key_is_not_stored_readable()
    {
        var clinician = await apps.CreateStaffAsync("Dr. Secret", StaffRoles.Clinician);

        await using var db = await apps.OpenDatabaseAsync();
        await using var command = new NpgsqlCommand(
            "SELECT value FROM ehr.staff_user_tokens WHERE user_id = @id AND name = 'AuthenticatorKey'", db);
        command.Parameters.AddWithValue("id", clinician.Id);
        var stored = (string?)await command.ExecuteScalarAsync(Cancel);

        Assert.NotNull(stored);
        Assert.NotEqual(clinician.AuthenticatorKey, stored);
    }
}
