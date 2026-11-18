using System.Globalization;
using Ehr.Data.Patients;
using Ehr.Domain.Patients;
using Ehr.Domain.Phi;
using Ehr.Web.Forwarding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Ehr.Web.Areas.Clinical.Pages.Patients.Lists;

// Adds or changes one entry of a patient's allergies, medications or diagnoses (the expansion plan's step 2; UC 4's
// chart). One page for the three lists, which differ only in their fields. The entry's name is picked from the
// reference codes as the clinician types (js/code-search.js fills the suggestions; without JavaScript the name is
// typed in full), and the form refuses a name that isn't one of them, so every new entry is coded. An entry recorded
// before there were codes keeps its name until it is changed. Nothing is deleted: an entry that no longer applies is
// given another status. The database keeps every change (ehr.chart_history) and records the write.
//
// The order form's rule: only for a patient on the clinician's panel; anyone else, a clinician under emergency access
// included, is sent back to the chart before anything is read. Off-site, the cloud app forwards the post; when it
// can't, its "Not saved" page shows what was typed again (UC 2, ShownIfNotSaved).
[ShownIfNotSaved(nameof(Name), nameof(Reaction), nameof(Dosage))]
public class EntryModel(PhiReader phi, PatientAssignments assignments, ChartLists lists) : PageModel
{
    // The three lists by their name in the address: the table, the kind of reference code, and what to call one entry.
    public static readonly IReadOnlyDictionary<string, (string Table, string Kind, string One, string Label)> Lists =
        new Dictionary<string, (string, string, string, string)>
        {
            ["allergies"] = ("allergy_intolerances", ClinicalCodeKind.Allergen, "allergy", "Substance"),
            ["medications"] = ("medication_statements", ClinicalCodeKind.Medication, "medication", "Medication"),
            ["diagnoses"] = ("conditions", ClinicalCodeKind.Condition, "diagnosis", "Diagnosis"),
        };

    [BindProperty(SupportsGet = true)]
    public SearchQuery Search { get; set; } = SearchQuery.None;

    [BindProperty(SupportsGet = true)]
    public string List { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public Guid? EntryId { get; set; }

    [BindProperty]
    public string? Name { get; set; }

    [BindProperty]
    public string? Status { get; set; }

    // Allergies.
    [BindProperty]
    public string? Reaction { get; set; }

    [BindProperty]
    public string? Severity { get; set; }

    [BindProperty]
    public string? Criticality { get; set; }

    // Medications.
    [BindProperty]
    public string? Dosage { get; set; }

    [BindProperty]
    public string? Started { get; set; }

    [BindProperty]
    public string? Stopped { get; set; }

    // Diagnoses.
    [BindProperty]
    public string? Onset { get; set; }

    // Medications and diagnoses.
    [BindProperty]
    public bool Sensitive { get; set; }

    public ChartEntryForm Form { get; private set; } = null!;

    public IReadOnlyDictionary<string, string> Errors { get; private set; } = new Dictionary<string, string>();

    public (string Table, string Kind, string One, string Label) About => Lists[List];

    public bool IsNew => EntryId is null;

    // The statuses a form offers, with the words the chart uses for them.
    public IReadOnlyList<(string Code, string Name)> Statuses => List switch
    {
        "allergies" => [(AllergyClinicalStatus.Active, "Active"), (AllergyClinicalStatus.Inactive, "Inactive"), (AllergyClinicalStatus.Resolved, "Resolved")],
        "medications" =>
        [
            (MedicationStatementStatus.Active, "Active"), (MedicationStatementStatus.OnHold, "On hold"), (MedicationStatementStatus.Stopped, "Stopped"),
            (MedicationStatementStatus.Completed, "Completed"), (MedicationStatementStatus.EnteredInError, "Entered in error"),
        ],
        _ =>
        [
            (ConditionClinicalStatus.Active, "Active"), (ConditionClinicalStatus.Recurrence, "Recurrence"), (ConditionClinicalStatus.Remission, "Remission"),
            (ConditionClinicalStatus.Inactive, "Inactive"), (ConditionClinicalStatus.Resolved, "Resolved"),
        ],
    };

    public Dictionary<string, string> ChartLink
    {
        get
        {
            var route = Search.RouteData();
            route["id"] = Form.Patient.Id.ToString();
            return route;
        }
    }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        if (await LoadAsync(id) is { } refused)
        {
            return refused;
        }

        if (Form.Allergy is { } allergy)
        {
            (Name, Status, Reaction, Severity, Criticality) = (allergy.Display, allergy.ClinicalStatus, allergy.Reaction, allergy.Severity, allergy.Criticality);
        }
        else if (Form.Medication is { } medication)
        {
            (Name, Status, Dosage, Sensitive) = (medication.Display, medication.Status, medication.Dosage, medication.Sensitive);
            (Started, Stopped) = (Date(medication.EffectiveStart), Date(medication.EffectiveEnd));
        }
        else if (Form.Condition is { } condition)
        {
            (Name, Status, Onset, Sensitive) = (condition.Display, condition.ClinicalStatus, Date(condition.OnsetDate), condition.Sensitive);
        }
        else
        {
            Status = Statuses[0].Code;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        Response.Headers.CacheControl = "no-store";
        if (await LoadAsync(id) is { } refused)
        {
            return refused;
        }

        var errors = new Dictionary<string, string>();
        var name = Name?.Trim() ?? "";
        var current = Form.Allergy?.Display ?? Form.Medication?.Display ?? Form.Condition?.Display;
        // An unchanged name keeps the code it has, or its lack of one; a new name must be one of the reference codes.
        var kept = current is not null && string.Equals(name, current, StringComparison.OrdinalIgnoreCase);
        var code = kept ? null : await lists.FindAsync(About.Kind, name, HttpContext.RequestAborted);
        if (name.Length == 0)
        {
            errors[nameof(Name)] = $"Enter the {About.Label.ToLowerInvariant()}.";
        }
        else if (!kept && code is null)
        {
            errors[nameof(Name)] = $"Choose a {About.Label.ToLowerInvariant()} from the list.";
        }

        if (!Statuses.Any(status => status.Code == Status))
        {
            errors[nameof(Status)] = "Choose a status.";
        }

        foreach (var (field, value) in new[] { (nameof(Reaction), Reaction), (nameof(Dosage), Dosage) })
        {
            if (value?.Trim().Length > ChartLists.MaximumLength)
            {
                errors[field] = $"Shorten this to {ChartLists.MaximumLength} characters or fewer.";
            }
        }

        if (Severity is not null && !AllergySeverity.All.Contains(Severity))
        {
            errors[nameof(Severity)] = "Choose a severity.";
        }

        if (Criticality is not null && !AllergyCriticality.All.Contains(Criticality))
        {
            errors[nameof(Criticality)] = "Choose a risk.";
        }

        var started = Parse(nameof(Started), Started, errors);
        var stopped = Parse(nameof(Stopped), Stopped, errors);
        var onset = Parse(nameof(Onset), Onset, errors);
        if (started is not null && stopped is not null && stopped < started)
        {
            errors[nameof(Stopped)] = "The stop date is before the start date.";
        }

        if (errors.Count > 0)
        {
            Errors = errors;
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return Page();
        }

        var entryId = EntryId ?? Guid.CreateVersion7();
        var saved = List switch
        {
            "allergies" => await lists.SaveAsync(new AllergyIntolerance
            {
                Id = entryId,
                PatientId = id,
                Code = kept ? Form.Allergy!.Code : code!.Code,
                CodeSystem = kept ? Form.Allergy!.CodeSystem : code!.System,
                Display = kept ? current! : code!.Display,
                ClinicalStatus = Status!,
                Category = kept ? Form.Allergy!.Category : code!.Category,
                Criticality = Criticality,
                Reaction = Blank(Reaction),
                Severity = Severity,
                RecordedAt = Form.Allergy?.RecordedAt ?? DateTimeOffset.UtcNow,
            }, IsNew, HttpContext.RequestAborted),
            "medications" => await lists.SaveAsync(new MedicationStatement
            {
                Id = entryId,
                PatientId = id,
                Code = kept ? Form.Medication!.Code : code!.Code,
                Display = kept ? current! : code!.Display,
                Dosage = Blank(Dosage),
                Status = Status!,
                EffectiveStart = started,
                EffectiveEnd = stopped,
                Sensitive = Sensitive,
            }, IsNew, HttpContext.RequestAborted),
            _ => await lists.SaveAsync(new Condition
            {
                Id = entryId,
                PatientId = id,
                Code = kept ? Form.Condition!.Code : code!.Code,
                Display = kept ? current! : code!.Display,
                ClinicalStatus = Status!,
                OnsetDate = onset,
                Sensitive = Sensitive,
            }, IsNew, HttpContext.RequestAborted),
        };
        if (!saved)
        {
            return RedirectToPage("/Patients/Chart", Search.ChartRouteValues(id));
        }

        return Redirect(Url.Page("/Patients/Chart", Search.ChartRouteValues(id)) + $"#chart-{List}");
    }

    static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    static string? Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly? Parse(string field, string? value, Dictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && date <= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)) && date.Year >= 1900)
        {
            return date;
        }

        errors[field] = "Enter a date that exists and isn't in the future, as year-month-day.";
        return null;
    }

    // The gate, then the one recorded read. Null when the form may be shown.
    async Task<IActionResult?> LoadAsync(Guid id)
    {
        if (!Lists.ContainsKey(List))
        {
            return NotFound();
        }

        if (!await assignments.IsOnPanelAsync(id, HttpContext.RequestAborted))
        {
            return RedirectToPage("/Patients/Chart", Search.ChartRouteValues(id));
        }

        if (await phi.ChartEntryAsync(id, About.Table, EntryId, HttpContext.RequestAborted) is not { } form)
        {
            return NotFound();
        }

        Form = form;
        // An entry that isn't this patient's, or doesn't exist: back to the chart, which lists what does.
        return EntryId is not null && form.Allergy is null && form.Medication is null && form.Condition is null
            ? RedirectToPage("/Patients/Chart", Search.ChartRouteValues(id))
            : null;
    }
}
