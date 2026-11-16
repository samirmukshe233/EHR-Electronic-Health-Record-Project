using System.ComponentModel.DataAnnotations;
using Ehr.Domain.Patients;
using Ehr.Web.Forwarding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ehr.Web.Areas.Clinical.Pages.Patients;

// Where the chart sends a clinician whose patient this isn't: the stated reason emergency access requires (BreakGlass).
// Nothing about the patient is shown here; the search results the clinician came from already were a recorded read.
// That search travels along to the chart (SearchQuery), for the way back to it. Off-site with the clinic's server
// unreachable, the reason typed is shown again on the "Not saved" page (UC 2). The reason is trimmed as it is bound,
// before it is checked, so spaces around a short reason don't make it long enough.
[ShownIfNotSaved(nameof(Reason))]
public class ReasonModel(BreakGlass breakGlass, PatientAssignments assignments) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public SearchQuery Search { get; set; } = SearchQuery.None;

    [BindProperty]
    [Required(ErrorMessage = "Say why you need this chart.")]
    [MinLength(BreakGlass.MinimumReasonLength, ErrorMessage = "Use at least 10 characters.")]
    [StringLength(BreakGlass.MaximumReasonLength, ErrorMessage = "Use 500 characters or fewer.")]
    public string? Reason { get; set => field = value?.Trim(); }

    public async Task<IActionResult> OnGetAsync(Guid id) =>
        await assignments.IsOnPanelAsync(id, HttpContext.RequestAborted) || await breakGlass.ActiveUntilAsync(id, HttpContext.RequestAborted) is not null
            ? RedirectToPage("Chart", Search.ChartRouteValues(id))
            : Page();

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await breakGlass.GrantAsync(id, Reason!, HttpContext.RequestAborted);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // No patient has this ID.
            return NotFound();
        }

        return RedirectToPage("Chart", Search.ChartRouteValues(id));
    }
}
