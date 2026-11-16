using System.Net;
using System.Text.Json;
using Ehr.Data.Accounts;
using Ehr.Data.Audit;
using Ehr.Data.Patients;
using Ehr.Web.CloudAudit;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static Ehr.Web.Tests.Browser;

namespace Ehr.Web.Tests;

// UC 1 Place an order (Tahseen Arnan): one test for each course of events, TC-UC1-01 to TC-UC1-07, through the order
// form (Areas/Clinical/Pages/Patients/Orders/New) and the chart, as the browser and js/orders.js send them. What the
// database refuses whatever the app does is in Ehr.Security.Tests (OrderSecurityTests); the script's own behaviour in a
// browser, the shortcut and keeping the form filled, is checked by hand.
public class OrderTests(WebApps apps) : IClassFixture<WebApps>
{
    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    static string FormPath(Guid patient, string type) => $"/patients/{patient}/orders/new?type={type}";

    async Task<(StaffAccount Clinician, Guid Patient, Session Session)> OnPanelAsync()
    {
        var clinician = await apps.ClinicianAsync();
        var patient = await apps.InsertPatientAsync("Synthetic", "Orderable");
        await apps.AssignAsync(clinician, patient);
        await apps.InsertAllergyAsync(patient, "Penicillin", criticality: AllergyCriticality.High, reaction: "Hives");
        return (clinician, patient, await SignInAsync(apps.OnPrem, clinician));
    }

    // "Sign order", as js/orders.js posts it (fetch) or as the form posts without JavaScript.
    static async Task<HttpResponseMessage> SignAsync(
        HttpClient client, Guid patient, string type, string item, (string Name, string Value)[] details, string? reason = null, bool fetch = true)
    {
        var token = Token(await client.GetStringAsync(FormPath(patient, type), Cancel));
        using var request = new HttpRequestMessage(HttpMethod.Post, FormPath(patient, type));
        if (fetch)
        {
            request.Headers.Add("HX-Request", "true");
        }

        request.Content = Form([("__RequestVerificationToken", token), ("Item", item),
            .. details.Select(d => ($"Details[{d.Name}]", (object)d.Value)), .. reason is null ? [] : new[] { ("DuplicateReason", (object)reason) }]);
        return await client.SendAsync(request, Cancel);
    }

    static readonly (string, string)[] Cbc = [("indication", "Synthetic fatigue")];

    async Task<long> OrdersAsync(Guid patient, string? catalogCode = null)
    {
        await using var db = await apps.OpenDatabaseAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM ehr.orders WHERE patient_id = @patient AND (@code::text IS NULL OR catalog_code = @code)", db);
        command.Parameters.AddWithValue("patient", patient);
        command.Parameters.AddWithValue("code", (object?)catalogCode ?? DBNull.Value);
        return (long)(await command.ExecuteScalarAsync(Cancel))!;
    }

    // TC-UC1-01, the typical course: "New order" (or Alt+Shift+O) opens the form with the patient's name and allergies;
    // the clinician picks a type, items are suggested and required fields marked; "Sign order" saves on the primary with
    // an audit entry naming the clinician and the time; the chart confirms the order and lists it as active.
    [Fact]
    public async Task TC_UC1_01_A_clinician_signs_an_order_and_the_chart_confirms_and_lists_it()
    {
        var (clinician, patient, session) = await OnPanelAsync();

        var chart = await session.Client.GetStringAsync($"/patients/{patient}", Cancel);
        Assert.Contains($"href=\"/patients/{patient}/orders/new\">New order</a>", chart);
        Assert.Contains("data-new-order aria-keyshortcuts=\"Alt+Shift+O\"", chart);
        Assert.Contains("/_content/Ehr.Design/js/orders.", chart);
        Assert.DoesNotContain("accesskey", chart);
        Assert.Contains("No active orders.", chart);

        var reads = await apps.AuditEventsAsync(AuditActions.Read, clinician.Id, patient);
        var form = await session.Client.GetStringAsync(FormPath(patient, "medication"), Cancel);
        Assert.Contains("New order: Orderable, Synthetic", form);
        Assert.Contains("<strong>Penicillin</strong>: Hives", form);
        Assert.Contains("High risk", form);
        Assert.Contains("<option value=\"Amoxicillin 500 mg capsule\"></option>", form);
        Assert.Matches(@"<label for=""order-dose"">Dose\s*<span>\(required\)</span>", form);
        Assert.Contains("name=\"Details[dose]\"", form);
        Assert.DoesNotContain("htmx.min.js", form);
        // The banner and allergies are one recorded read.
        Assert.Equal(reads + 1, await apps.AuditEventsAsync(AuditActions.Read, clinician.Id, patient));

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        using var signed = await SignAsync(session.Client, patient, "medication", "amoxicillin 500 MG capsule",
            [("dose", "500 mg"), ("route", "By mouth"), ("frequency", "Three times daily")]);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        var url = JsonDocument.Parse(await signed.Content.ReadAsStringAsync(Cancel)).RootElement.GetProperty("url").GetString()!;

        var confirmed = await session.Client.GetStringAsync(url, Cancel);
        Assert.Contains("Order signed: <strong>Amoxicillin 500 mg capsule</strong>", confirmed);
        Assert.Contains("Dose: 500 mg; Route: By mouth; Frequency: Three times daily", confirmed);
        Assert.DoesNotContain("No active orders.", confirmed);

        await using var db = await apps.OpenDatabaseAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT o.signer_id, o.signed_at, e.actor_id, e.occurred_at = o.signed_at
            FROM ehr.orders o JOIN ehr.audit_events e ON e.row_id = o.id AND e.action = 'insert' AND e.table_name = 'orders'
            WHERE o.patient_id = @patient
            """, db);
        command.Parameters.AddWithValue("patient", patient);
        await using var reader = await command.ExecuteReaderAsync(Cancel);
        Assert.True(await reader.ReadAsync(Cancel));
        Assert.Equal(clinician.Id, reader.GetGuid(0));
        Assert.InRange(reader.GetFieldValue<DateTimeOffset>(1), before, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(clinician.Id, reader.GetGuid(2));
        Assert.True(reader.GetBoolean(3));
        Assert.False(await reader.ReadAsync(Cancel));
    }

    // TC-UC1-02: a role that can't order sees no "New order" and is refused. An admin is refused the form; a clinician
    // under emergency access is told to add the patient to their panel first, and sent back to the chart before anything
    // is read or saved. The database's refusal of their inserts is OrderSecurityTests'.
    [Fact]
    public async Task TC_UC1_02_A_role_that_cannot_order_sees_no_new_order_and_is_refused()
    {
        var patient = await apps.InsertPatientAsync("Synthetic", "Unordered");
        var admin = await SignInAsync(apps.OnPrem, await apps.CreateStaffAsync("Order Test Admin", StaffRoles.Admin));
        using var asAdmin = await Client(apps.OnPrem, admin.Jar, followRedirects: false).GetAsync(FormPath(patient, "lab"), Cancel);
        Assert.Equal("/denied", new Uri(Base, asAdmin.Headers.Location!).AbsolutePath);

        var covering = await apps.CreateStaffAsync("Dr. Covering", StaffRoles.Clinician);
        var session = await SignInAsync(apps.OnPrem, covering);
        using var opened = await PostFormAsync(session.Client, $"/patients/{patient}/reason", $"/patients/{patient}/reason", ("Reason", "Covering the night shift"));
        var chart = await ReadAsync(opened);
        Assert.DoesNotContain("data-new-order", chart);
        Assert.Contains("Add this patient to your panel to place an order.", chart);

        var reads = await apps.AuditEventsAsync(AuditActions.Read, covering.Id, patient);
        var noRedirects = Client(apps.OnPrem, session.Jar, followRedirects: false);
        using var form = await noRedirects.GetAsync(FormPath(patient, "lab"), Cancel);
        Assert.Equal($"/patients/{patient}", new Uri(Base, form.Headers.Location!).AbsolutePath);
        Assert.Equal(reads, await apps.AuditEventsAsync(AuditActions.Read, covering.Id, patient));

        var token = Token(chart);
        using var post = await noRedirects.PostAsync(FormPath(patient, "lab"),
            Form(("__RequestVerificationToken", token), ("Item", "Complete blood count (CBC)"), ("Details[indication]", "Synthetic")), Cancel);
        Assert.Equal($"/patients/{patient}", new Uri(Base, post.Headers.Location!).AbsolutePath);
        Assert.Equal(0, await OrdersAsync(patient));
    }

    // TC-UC1-03: an empty required field is highlighted and nothing is saved; what was entered stays.
    [Fact]
    public async Task TC_UC1_03_An_empty_required_field_is_highlighted_and_nothing_is_saved()
    {
        var (_, patient, session) = await OnPanelAsync();

        using var response = await SignAsync(session.Client, patient, "lab", "Complete blood count (CBC)", [("indication", " "), ("notes", "Fasting")]);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(Cancel);
        Assert.Contains("id=\"order-indication\"", html);
        Assert.Matches("id=\"order-indication\"[^>]* aria-invalid=\"true\"", html);
        Assert.Contains("<p class=\"error\" id=\"order-indication-error\">Enter the reason for the test.</p>", html);
        Assert.Contains("value=\"Complete blood count (CBC)\"", html);
        Assert.Contains("value=\"Fasting\"", html);
        Assert.Equal(0, await OrdersAsync(patient));
    }

    // TC-UC1-04: a match with an active order shows a warning, and nothing is saved until the clinician gives a reason.
    // Two submissions at once save one order; the other gets the same warning.
    [Fact]
    public async Task TC_UC1_04_A_duplicate_active_order_warns_and_needs_a_reason()
    {
        var (_, patient, session) = await OnPanelAsync();
        using var first = await SignAsync(session.Client, patient, "lab", "Complete blood count (CBC)", Cbc);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var warned = await SignAsync(session.Client, patient, "lab", "Complete blood count (CBC)", Cbc);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, warned.StatusCode);
        var html = await warned.Content.ReadAsStringAsync(Cancel);
        Assert.Contains("data-order-duplicate", html);
        Assert.Contains("An active order for <strong>Complete blood count (CBC)</strong> was signed", html);
        Assert.Contains("name=\"DuplicateReason\"", html);
        Assert.Contains("value=\"Synthetic fatigue\"", html);
        Assert.Equal(1, await OrdersAsync(patient));

        using var confirmed = await SignAsync(session.Client, patient, "lab", "Complete blood count (CBC)", Cbc, reason: "Synthetic repeat: sample clotted");
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(2, await OrdersAsync(patient, "SYN-LAB-001"));

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => SignAsync(session.Client, patient, "lab", "Lipid panel", Cbc)));
        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
            Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity }));
            Assert.Equal(1, await OrdersAsync(patient, "SYN-LAB-004"));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    // TC-UC1-05: off-site, the order is forwarded to the on-site server, which saves it.
    [Fact]
    public async Task TC_UC1_05_Off_site_the_order_is_forwarded_and_saved_on_site()
    {
        var (_, patient, session) = await OnPanelAsync();
        var cloud = Client(apps.Cloud, session.Jar);

        using var signed = await SignAsync(cloud, patient, "imaging", "Chest X-ray, 2 views", [("indication", "Synthetic cough")]);

        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        Assert.Contains($"POST {FormPath(patient, "imaging")}", apps.Forwarded);
        Assert.Equal(1, await OrdersAsync(patient, "SYN-IMG-001"));
    }

    // TC-UC1-06: off-site with the on-site server unreachable, the chart offers no "New order", and a submitted order is
    // not saved: fetch gets a 503, on which js/orders.js keeps the form filled and says so; a plain post gets the "Not
    // saved" page with a way back to the form.
    [Fact]
    public async Task TC_UC1_06_Off_site_with_the_clinics_server_down_the_order_is_not_saved()
    {
        var (_, patient, session) = await OnPanelAsync();
        Assert.False(await apps.CloudCutOff.Services.GetRequiredService<CloudAuditShipper>().ShipAsync(Cancel));
        var cutOff = Client(apps.CloudCutOff, session.Jar);

        var chart = await cutOff.GetStringAsync($"/patients/{patient}", Cancel);
        Assert.DoesNotContain("data-new-order", chart);
        Assert.Contains("New orders need the clinic's server.", chart);
        Assert.Contains("/_content/Ehr.Design/js/orders.", await cutOff.GetStringAsync(FormPath(patient, "lab"), Cancel));

        using var fetched = await SignAsync(cutOff, patient, "lab", "Urinalysis", Cbc);
        using var posted = await SignAsync(cutOff, patient, "lab", "Urinalysis", Cbc, fetch: false);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, fetched.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, posted.StatusCode);
        Assert.Contains("Nothing was saved", await posted.Content.ReadAsStringAsync(Cancel));
        Assert.Equal(0, await OrdersAsync(patient));
    }

    // TC-UC1-07: Cancel goes back to the chart and saves nothing.
    [Fact]
    public async Task TC_UC1_07_Cancel_saves_nothing()
    {
        var (_, patient, session) = await OnPanelAsync();

        var form = await session.Client.GetStringAsync(FormPath(patient, "referral"), Cancel);
        Assert.Contains($"<a href=\"/patients/{patient}\">Cancel</a>", form);
        var chart = await session.Client.GetStringAsync($"/patients/{patient}", Cancel);

        Assert.Contains("No active orders.", chart);
        Assert.Equal(0, await OrdersAsync(patient));
    }
}
