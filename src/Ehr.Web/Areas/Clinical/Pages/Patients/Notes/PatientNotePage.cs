using Ehr.Data.Patients;
using Ehr.Domain.Patients;
using Ehr.Domain.Phi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Ehr.Web.Areas.Clinical.Pages.Patients.Notes;

// The same chart gate as ChartModel, before reading any note or its patient identity.
public abstract class PatientNotePage(PhiReader phi, PatientAssignments assignments, BreakGlass breakGlass) : PageModel
{
    protected PhiReader Phi { get; } = phi;

    public Patient Patient { get; private set; } = null!;

    // The patient's current allergies, for the banner: part of the same recorded read as the patient.
    public IReadOnlyList<AllergyIntolerance> Allergies { get; private set; } = [];

    protected async Task<IActionResult?> LoadPatientAsync(Guid patientId)
    {
        Response.Headers.CacheControl = "no-store";
        if (!await assignments.IsOnPanelAsync(patientId, HttpContext.RequestAborted)
            && await breakGlass.ActiveUntilAsync(patientId, HttpContext.RequestAborted) is null)
        {
            return RedirectToPage("/Patients/Reason", new { id = patientId });
        }

        var header = await Phi.HeaderAsync(patientId, HttpContext.RequestAborted);
        if (header is null)
        {
            return NotFound();
        }

        (Patient, Allergies) = (header.Patient, header.Allergies);
        return null;
    }
}
