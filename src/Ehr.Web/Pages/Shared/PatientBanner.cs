using Ehr.Data.Patients;

namespace Ehr.Web;

// What the patient banner shows (Pages/Shared/_PatientBanner.cshtml): the strip under the app bar on every clinician
// page about a patient. Allergies are the page's own, already read with its record of the read: a page that hasn't
// read them passes none, and the banner then shows no allergy area at all rather than a wrong "none recorded".
// Title makes the name the page's heading, on the chart, where the patient is the title.
public sealed record PatientBanner(
    Patient Patient,
    IReadOnlyList<AllergyIntolerance>? Allergies = null,
    bool OnPanel = false,
    DateTimeOffset? EmergencyUntil = null,
    bool Title = false)
{
    public IReadOnlyList<AllergyIntolerance> Known { get; } =
        Allergies is null ? [] : [.. Allergies.Where(allergy => allergy.Code != AllergyIntolerance.NoKnownAllergy)];

    // "No known allergies" is recorded as a row of its own, which is not the same as nothing recorded.
    public bool NoneKnown => Allergies is not null && Known.Count == 0 && Allergies.Count > 0;
}
