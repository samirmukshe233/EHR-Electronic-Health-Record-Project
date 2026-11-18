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

// UC 1 Place an order (Tahseen Arnan): the order form's helpers, and extra coverage beside the Assignment 4 test cases,
// which are one file each in TestCases/UC1 (TC-UC1-01 to TC-UC1-06): a role that can't order, an off-site order that is
// forwarded, and Cancel; and, for changing and discontinuing an order (TC-UC1-07 to TC-UC1-12), who is offered them,
// the off-site path, and a link to an order that is no longer active. Through the order form (Areas/Clinical/Pages/Patients/Orders/New) and the chart, as the browser
// and js/orders.js send them. What the database refuses whatever the app does is in Ehr.Security.Tests
// (OrderSecurityTests).
public class OrderTests(WebApps apps) : IClassFixture<WebApps>
{
    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    internal static string FormPath(Guid patient, string type) => $"/patients/{patient}/orders/new?type={type}";

    internal static async Task<(StaffAccount Clinician, Guid Patient, Session Session)> OnPanelAsync(WebApps apps)
    {
        var clinician = await apps.ClinicianAsync();
        var patient = await apps.InsertPatientAsync("Synthetic", "Orderable");
        await apps.AssignAsync(clinician, patient);
        await apps.InsertAllergyAsync(patient, "Penicillin", criticality: AllergyCriticality.High, reaction: "Hives");
        return (clinician, patient, await SignInAsync(apps.OnPrem, clinician));
    }

    // "Sign order", as js/orders.js posts it (fetch) or as the form posts without JavaScript.
    internal static async Task<HttpResponseMessage> SignAsync(
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

    internal static readonly (string, string)[] Cbc = [("indication", "Synthetic fatigue")];

    internal static string DiscontinuePath(Guid patient, Guid order) => $"/patients/{patient}/orders/{order}/discontinue";

    internal static string ChangePath(Guid patient, string type, Guid order) => $"{FormPath(patient, type)}&replaces={order}";

    // Signs an order and returns its ID, from the address the order form sends the browser to.
    internal static async Task<Guid> SignedAsync(HttpClient client, Guid patient, string type, string item, (string Name, string Value)[] details)
    {
        using var signed = await SignAsync(client, patient, type, item, details);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        var url = JsonDocument.Parse(await signed.Content.ReadAsStringAsync(Cancel)).RootElement.GetProperty("url").GetString()!;
        return Guid.Parse(System.Web.HttpUtility.ParseQueryString(new Uri(Base, url).Query)["ordered"]!);
    }

    // "Discontinue order", as js/orders.js posts it (fetch) or as the form posts without JavaScript. The form token is
    // the page's own unless one read earlier is given, for a post that arrives after the order changed.
    internal static async Task<HttpResponseMessage> DiscontinueAsync(
        HttpClient client, Guid patient, Guid order, string? reason, bool fetch = true, string? token = null)
    {
        token ??= Token(await client.GetStringAsync(DiscontinuePath(patient, order), Cancel));
        using var request = new HttpRequestMessage(HttpMethod.Post, DiscontinuePath(patient, order));
        // The browser says the plain form is the site's own, which is when "Not saved" shows what was typed again.
        request.Headers.Add(fetch ? "HX-Request" : "Sec-Fetch-Site", fetch ? "true" : "same-origin");

        request.Content = Form([("__RequestVerificationToken", token), .. reason is null ? [] : new[] { ("Reason", (object)reason) }]);
        return await client.SendAsync(request, Cancel);
    }

    // "Sign changed order", as js/orders.js posts it.
    internal static async Task<HttpResponseMessage> ChangeAsync(
        HttpClient client, Guid patient, string type, Guid order, string item, (string Name, string Value)[] details, string? changeReason = null,
        string? duplicateReason = null, string? token = null)
    {
        token ??= Token(await client.GetStringAsync(ChangePath(patient, type, order), Cancel));
        using var request = new HttpRequestMessage(HttpMethod.Post, ChangePath(patient, type, order));
        request.Headers.Add("HX-Request", "true");
        request.Content = Form([("__RequestVerificationToken", token), ("Item", item),
            .. details.Select(d => ($"Details[{d.Name}]", (object)d.Value)),
            .. changeReason is null ? [] : new[] { ("ChangeReason", (object)changeReason) },
            .. duplicateReason is null ? [] : new[] { ("DuplicateReason", (object)duplicateReason) }]);
        return await client.SendAsync(request, Cancel);
    }

    // An order's row as "status|reason|discontinuer|replaces|replaced by", "-" for a null.
    internal static async Task<string> RowAsync(WebApps apps, Guid order)
    {
        await using var db = await apps.OpenDatabaseAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT concat_ws('|', status, coalesce(discontinue_reason, '-'), coalesce(discontinuer_id::text, '-'), coalesce(replaces_order_id::text, '-'),
                coalesce(replaced_by_order_id::text, '-'))
            FROM ehr.orders WHERE id = @id
            """, db);
        command.Parameters.AddWithValue("id", order);
        return (string)(await command.ExecuteScalarAsync(Cancel))!;
    }

    internal static async Task<long> ActiveAsync(WebApps apps, Guid patient)
    {
        await using var db = await apps.OpenDatabaseAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM ehr.orders WHERE patient_id = @patient AND status = 'active'", db);
        command.Parameters.AddWithValue("patient", patient);
        return (long)(await command.ExecuteScalarAsync(Cancel))!;
    }

    internal static async Task<long> OrdersAsync(WebApps apps, Guid patient, string? catalogCode = null)
    {
        await using var db = await apps.OpenDatabaseAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM ehr.orders WHERE patient_id = @patient AND (@code::text IS NULL OR catalog_code = @code)", db);
        command.Parameters.AddWithValue("patient", patient);
        command.Parameters.AddWithValue("code", (object?)catalogCode ?? DBNull.Value);
        return (long)(await command.ExecuteScalarAsync(Cancel))!;
    }

    // A role that can't order sees no "New order" and is refused. An admin is refused the form; a clinician
    // under emergency access is told to add the patient to their panel first, and sent back to the chart before anything
    // is read or saved. The database's refusal of their inserts is OrderSecurityTests'.
    [Fact]
    public async Task A_role_that_cannot_order_sees_no_new_order_and_is_refused()
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
        Assert.Equal(0, await OrdersAsync(apps, patient));
    }

    // Off-site, the order is forwarded to the on-site server, which saves it.
    [Fact]
    public async Task Off_site_the_order_is_forwarded_and_saved_on_site()
    {
        var (_, patient, session) = await OnPanelAsync(apps);
        var cloud = Client(apps.Cloud, session.Jar);

        using var signed = await SignAsync(cloud, patient, "imaging", "Chest X-ray, 2 views", [("indication", "Synthetic cough")]);

        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        Assert.Contains($"POST {FormPath(patient, "imaging")}", apps.Forwarded);
        Assert.Equal(1, await OrdersAsync(apps, patient, "SYN-IMG-001"));
    }

    // UC 1, changing and discontinuing: only a clinician with the patient on their panel is offered either, and anyone
    // else is sent back to the chart before anything is read or saved. Under emergency access the discontinued orders
    // can still be read, as the chart can.
    [Fact]
    public async Task A_clinician_off_the_panel_is_offered_no_change_or_discontinue_and_is_refused()
    {
        var (_, patient, session) = await OnPanelAsync(apps);
        var order = await SignedAsync(session.Client, patient, "lab", "Complete blood count (CBC)", Cbc);
        using var stopped = await DiscontinueAsync(session.Client, patient, await SignedAsync(session.Client, patient, "lab", "Urinalysis", Cbc), "Synthetic: sample lost");
        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);

        var covering = await apps.CreateStaffAsync("Dr. Covering Orders", StaffRoles.Clinician);
        var other = await SignInAsync(apps.OnPrem, covering);
        using var opened = await PostFormAsync(other.Client, $"/patients/{patient}/reason", $"/patients/{patient}/reason", ("Reason", "Covering the night shift"));
        var chart = await ReadAsync(opened);
        Assert.Contains("<strong>Complete blood count (CBC)</strong> (Lab)", chart);
        Assert.DoesNotContain("data-change-order", chart);
        Assert.DoesNotContain("data-discontinue-order", chart);
        Assert.Contains($"href=\"/patients/{patient}/orders/discontinued\">Discontinued orders</a>", chart);

        var listed = await other.Client.GetStringAsync($"/patients/{patient}/orders/discontinued", Cancel);
        Assert.Contains("<strong>Urinalysis</strong> (Lab)", listed);
        Assert.Contains("Synthetic: sample lost", listed);

        var reads = await apps.AuditEventsAsync(AuditActions.Read, covering.Id, patient);
        var noRedirects = Client(apps.OnPrem, other.Jar, followRedirects: false);
        foreach (var path in new[] { DiscontinuePath(patient, order), ChangePath(patient, "lab", order) })
        {
            using var form = await noRedirects.GetAsync(path, Cancel);
            Assert.Equal($"/patients/{patient}", new Uri(Base, form.Headers.Location!).AbsolutePath);
        }

        using var discontinue = await DiscontinueAsync(noRedirects, patient, order, "Synthetic", token: Token(chart));
        Assert.Equal($"/patients/{patient}", new Uri(Base, discontinue.Headers.Location!).AbsolutePath);
        using var change = await ChangeAsync(noRedirects, patient, "lab", order, "Lipid panel", Cbc, token: Token(chart));
        Assert.Equal($"/patients/{patient}", new Uri(Base, change.Headers.Location!).AbsolutePath);
        Assert.Equal(reads, await apps.AuditEventsAsync(AuditActions.Read, covering.Id, patient));
        Assert.StartsWith("active|", await RowAsync(apps, order));

        // An admin has no patient pages at all.
        var admin = await SignInAsync(apps.OnPrem, await apps.CreateStaffAsync("Order Change Admin", StaffRoles.Admin));
        using var asAdmin = await Client(apps.OnPrem, admin.Jar, followRedirects: false).GetAsync($"/patients/{patient}/orders/discontinued", Cancel);
        Assert.Equal("/denied", new Uri(Base, asAdmin.Headers.Location!).AbsolutePath);
    }

    // Off-site, discontinuing and changing are forwarded to the on-site server, which saves them; the discontinued
    // orders are read from the cloud copy.
    [Fact]
    public async Task Off_site_a_discontinuation_and_a_change_are_forwarded_and_saved_on_site()
    {
        var (_, patient, session) = await OnPanelAsync(apps);
        var cloud = Client(apps.Cloud, session.Jar);
        var first = await SignedAsync(session.Client, patient, "imaging", "Chest X-ray, 2 views", [("indication", "Synthetic cough")]);
        var second = await SignedAsync(session.Client, patient, "lab", "Lipid panel", Cbc);

        using var discontinued = await DiscontinueAsync(cloud, patient, first, "Synthetic: cough resolved");
        using var changed = await ChangeAsync(cloud, patient, "lab", second, "Lipid panel", [("indication", "Synthetic: fasting sample")]);

        Assert.Equal(HttpStatusCode.OK, discontinued.StatusCode);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Contains($"POST {DiscontinuePath(patient, first)}", apps.Forwarded);
        Assert.StartsWith("revoked|Synthetic: cough resolved|", await RowAsync(apps, first));
        Assert.StartsWith($"revoked|{Ehr.Domain.Orders.ClinicalOrders.ReplacedReason}|", await RowAsync(apps, second));
        Assert.Equal(1, await ActiveAsync(apps, patient));
    }

    // A link to change or discontinue an order that is no longer active goes back to the chart, which lists what is.
    [Fact]
    public async Task A_link_to_an_order_that_is_no_longer_active_goes_back_to_the_chart()
    {
        var (_, patient, session) = await OnPanelAsync(apps);
        var order = await SignedAsync(session.Client, patient, "lab", "Complete blood count (CBC)", Cbc);
        using var stopped = await DiscontinueAsync(session.Client, patient, order, "Synthetic");
        var noRedirects = Client(apps.OnPrem, session.Jar, followRedirects: false);

        foreach (var path in new[] { DiscontinuePath(patient, order), ChangePath(patient, "lab", order), ChangePath(patient, "lab", Guid.NewGuid()) })
        {
            using var form = await noRedirects.GetAsync(path, Cancel);
            Assert.Equal(HttpStatusCode.Redirect, form.StatusCode);
            Assert.Equal($"/patients/{patient}", new Uri(Base, form.Headers.Location!).AbsolutePath);
        }
    }

    // Cancel goes back to the chart and saves nothing.
    [Fact]
    public async Task Cancel_saves_nothing()
    {
        var (_, patient, session) = await OnPanelAsync(apps);

        var form = await session.Client.GetStringAsync(FormPath(patient, "referral"), Cancel);
        Assert.Contains($"<a href=\"/patients/{patient}\">Cancel</a>", form);
        var chart = await session.Client.GetStringAsync($"/patients/{patient}", Cancel);

        Assert.Contains("No active orders.", chart);
        Assert.Equal(0, await OrdersAsync(apps, patient));
    }
}
