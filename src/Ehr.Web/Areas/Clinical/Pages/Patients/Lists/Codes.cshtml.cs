using Ehr.Data.Patients;
using Ehr.Domain.Patients;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Ehr.Web.Areas.Clinical.Pages.Patients.Lists;

// The reference codes that match what a clinician has typed, as the options of the entry form's list
// (js/code-search.js). Reference data, not PHI: nothing about a patient is read or sent, so nothing is recorded. A
// page of the clinical area, so the area's policy covers it: clinicians only.
public class CodesModel(ChartLists lists) : PageModel
{
    public IReadOnlyList<ClinicalCode> Codes { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string? kind, string? q)
    {
        Codes = await lists.SearchAsync(kind ?? "", q, HttpContext.RequestAborted);
        return Page();
    }
}
