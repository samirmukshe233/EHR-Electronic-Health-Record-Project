using Ehr.Data.Orders;
using Ehr.Domain.Orders;
using Ehr.Domain.Patients;
using Ehr.Domain.Phi;
using Ehr.Web.Forwarding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Ehr.Web.Areas.Clinical.Pages.Patients.Orders;

// UC 1 Place an order (Tahseen Arnan). The order form: the patient's name and allergies at the top, one recorded read
// through the PHI reader; the type, picked by link; the item, suggested from the type's catalog as the clinician types
// (a datalist); and the type's details, the required ones marked. "Sign order" checks for missing fields and for an
// active order of the same item, which needs a reason; the database checks the duplicate again, under a lock, sets the
// signer and the time, and records the write. Then the chart confirms the order and lists it. Cancel is a link back to
// the chart, which saves nothing.
//
// The same rule as the chart's "New note": only for a patient on the clinician's panel. Anyone else, a clinician under
// emergency access included, is sent back to the chart before anything about the patient is read. Off-site, the cloud
// app forwards the post to the on-prem app; js/orders.js posts it with fetch, so when that fails the form stays filled.
// Without JavaScript, the cloud app's "Not saved" page shows the order's fields again to copy (UC 2, ShownIfNotSaved).
[ShownIfNotSaved(nameof(Item), "Details[indication]", "Details[dose]", "Details[route]", "Details[frequency]", "Details[duration]",
    "Details[reason]", "Details[notes]", nameof(DuplicateReason))]
public class NewModel(PhiReader phi, PatientAssignments assignments, ClinicalOrders orders) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public SearchQuery Search { get; set; } = SearchQuery.None;

    // One of OrderType, from the query string; anything else shows only the choice of type.
    [BindProperty(SupportsGet = true)]
    public string? Type { get; set; }

    [BindProperty]
    public string? Item { get; set; }

    [BindProperty]
    public Dictionary<string, string?> Details { get; set; } = [];

    [BindProperty]
    public string? DuplicateReason { get; set; }

    public OrderForm Form { get; private set; } = null!;

    public IReadOnlyList<OrderCatalogItem> Catalog { get; private set; } = [];

    // What to fix, by field: "Item", a detail's name, or "DuplicateReason".
    public IReadOnlyDictionary<string, string> Errors { get; private set; } = new Dictionary<string, string>();

    public string? KnownType => Type is not null && OrderForms.Fields.ContainsKey(Type) ? Type : null;

    // The way back to the chart, with the search the clinician came from.
    public Dictionary<string, string> ChartLink
    {
        get
        {
            var route = Search.RouteData();
            route["id"] = Form.Patient.Id.ToString();
            return route;
        }
    }

    public async Task<IActionResult> OnGetAsync(Guid id) => await LoadAsync(id, catalogCode: null) ?? Page();

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        Response.Headers.CacheControl = "no-store";
        if (KnownType is not { } type)
        {
            return BadRequest();
        }

        var (item, errors) = await orders.CheckAsync(type, Item, Details, HttpContext.RequestAborted);
        if (await LoadAsync(id, item?.Code) is { } refused)
        {
            return refused;
        }

        var reason = string.IsNullOrWhiteSpace(DuplicateReason) ? null : DuplicateReason.Trim();
        if (Form.Duplicates.Count > 0 && reason is null)
        {
            errors["DuplicateReason"] = "Give a reason to order this again, or cancel.";
        }
        else if (reason?.Length > OrderForms.MaximumReasonLength)
        {
            errors["DuplicateReason"] = $"Shorten the reason to {OrderForms.MaximumReasonLength} characters or fewer.";
        }

        if (errors.Count > 0)
        {
            return Refused(errors);
        }

        var order = await orders.SignAsync(id, item!, OrderForms.Details(type, Details), Form.Duplicates.Count > 0 ? reason : null,
            HttpContext.RequestAborted);
        if (order is null)
        {
            // Another order for the same item was signed at the same moment: warn as for any duplicate.
            await LoadAsync(id, item!.Code);
            return Refused(new Dictionary<string, string> { ["DuplicateReason"] = "Give a reason to order this again, or cancel." });
        }

        var chart = Search.ChartRouteValues(id);
        chart["ordered"] = order.Id;
        var url = Url.Page("/Patients/Chart", chart)!;
        return Request.Headers.ContainsKey("HX-Request") ? new JsonResult(new { url }) : Redirect(url);
    }

    // Nothing saved: the form again, with what was entered and what to fix.
    PageResult Refused(Dictionary<string, string> errors)
    {
        Errors = errors;
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return Page();
    }

    // The gate, then the one recorded read. Null when the form may be shown.
    async Task<IActionResult?> LoadAsync(Guid id, string? catalogCode)
    {
        if (!await assignments.IsOnPanelAsync(id, HttpContext.RequestAborted))
        {
            return RedirectToPage("/Patients/Chart", Search.ChartRouteValues(id));
        }

        if (await phi.OrderFormAsync(id, catalogCode, HttpContext.RequestAborted) is not { } form)
        {
            return NotFound();
        }

        Form = form;
        Catalog = KnownType is { } type ? await orders.CatalogAsync(type, HttpContext.RequestAborted) : [];
        return null;
    }
}
