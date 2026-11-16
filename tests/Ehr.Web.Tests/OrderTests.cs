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
// forwarded, and Cancel. Through the order form (Areas/Clinical/Pages/Patients/Orders/New) and the chart, as the browser
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
