using Ehr.Data;
using Ehr.Data.Patients;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ehr.Domain.Patients;

// Adding to and changing a patient's allergies, medications and diagnoses (the expansion plan's step 2), and the
// reference codes their entries are picked from. Called inside the page's database transaction
// (DatabaseTransactionFilter). The reference codes aren't PHI, so they are read here; the patient and the entry being
// changed are read through PhiReader. Who may write is the page's rule and the database's (a clinician, for a patient
// on their panel: db/policies/30-rls.sql); the database also keeps the history of every change, marks a diagnosis in a
// sensitive range sensitive whatever is sent, and records the write (65-chart-lists.sql, 40-audit.sql). Nothing
// deletes an entry: it is marked inactive, resolved or stopped.
public sealed class ChartLists(EhrDbContext db)
{
    // How many names the search offers as a clinician types.
    public const int Suggestions = 20;

    public const int MaximumLength = 200;

    // What a patient can have when exposed, offered as the clinician types; anything else can be typed.
    public static readonly string[] Reactions =
    [
        "Anaphylaxis", "Contact dermatitis", "Diarrhea", "Hives", "Itching", "Nausea", "Rash", "Shortness of breath", "Sneezing",
        "Stomach cramps", "Swelling of the lips", "Vomiting", "Wheezing",
    ];

    // What the chart calls an allergy entry.
    public static string DisplayOf(AllergyIntolerance allergy) => allergy.Display;

    // Names of one kind that match what was typed: first those that start with it, then those with a word that does,
    // then a diagnosis whose code starts with it. Nothing for fewer than two characters.
    public async Task<IReadOnlyList<ClinicalCode>> SearchAsync(string kind, string? typed, CancellationToken cancellationToken)
    {
        var key = typed?.Trim().ToLowerInvariant() ?? "";
        if (key.Length < 2 || !ClinicalCodeKind.All.Contains(kind))
        {
            return [];
        }

        var pattern = Like(key);
        return await db.ClinicalCodes.AsNoTracking()
            .Where(c => c.Kind == kind && (EF.Functions.Like(c.DisplayKey, pattern + "%") || EF.Functions.Like(c.DisplayKey, "% " + pattern + "%")
                || EF.Functions.ILike(c.Code, pattern + "%")))
            .OrderBy(c => EF.Functions.Like(c.DisplayKey, pattern + "%") ? 0 : 1).ThenBy(c => c.DisplayKey).ThenBy(c => c.Code)
            .Take(Suggestions)
            .ToListAsync(cancellationToken);
    }

    // Whether a diagnosis's ICD-10-CM code is sensitive by the rule (ehr.icd10cm_is_sensitive), as the loaded reference
    // codes record it: the entry form shows such a diagnosis's "Sensitive" box checked and fixed. The database marks it
    // whatever the form sends, so a code missing from the reference list is still marked; the form just can't say so.
    public Task<bool> IsSensitiveDiagnosisAsync(string? code, CancellationToken cancellationToken) =>
        code is null
            ? Task.FromResult(false)
            : db.ClinicalCodes.AsNoTracking().AnyAsync(c => c.Kind == ClinicalCodeKind.Condition && c.Code == code && c.Sensitive, cancellationToken);

    // The code whose name is exactly what was picked or typed, in any case.
    public Task<ClinicalCode?> FindAsync(string kind, string? display, CancellationToken cancellationToken)
    {
        var key = display?.Trim().ToLowerInvariant() ?? "";
        return db.ClinicalCodes.AsNoTracking().Where(c => c.Kind == kind && c.DisplayKey == key).OrderBy(c => c.System).ThenBy(c => c.Code)
            .FirstOrDefaultAsync(cancellationToken);
    }

    // Adds the entry, or, when it has an ID already, saves every field of it a form can change. Refused when the
    // database refused: the patient isn't on the clinician's panel, or the entry isn't theirs to change. Nothing is
    // saved then.
    public Task<ChartSave> SaveAsync(AllergyIntolerance allergy, bool isNew, CancellationToken cancellationToken) =>
        SaveAsync(allergy, isNew, cancellationToken, nameof(AllergyIntolerance.Code), nameof(AllergyIntolerance.CodeSystem),
            nameof(AllergyIntolerance.Display), nameof(AllergyIntolerance.ClinicalStatus), nameof(AllergyIntolerance.Category),
            nameof(AllergyIntolerance.Criticality), nameof(AllergyIntolerance.Reaction), nameof(AllergyIntolerance.Severity));

    public Task<ChartSave> SaveAsync(MedicationStatement medication, bool isNew, CancellationToken cancellationToken) =>
        SaveAsync(medication, isNew, cancellationToken, nameof(MedicationStatement.Code), nameof(MedicationStatement.Display),
            nameof(MedicationStatement.Dosage), nameof(MedicationStatement.Status), nameof(MedicationStatement.EffectiveStart),
            nameof(MedicationStatement.EffectiveEnd), nameof(MedicationStatement.Sensitive));

    public Task<ChartSave> SaveAsync(Condition condition, bool isNew, CancellationToken cancellationToken) =>
        SaveAsync(condition, isNew, cancellationToken, nameof(Condition.Code), nameof(Condition.Display), nameof(Condition.ClinicalStatus),
            nameof(Condition.OnsetDate), nameof(Condition.Sensitive));

    // Records that the patient has no known allergies: an entry of its own, so a chart can tell it from no allergy
    // information at all. The database refuses it for a patient with an active allergy recorded, and makes it inactive
    // when one is added (db/policies/65-chart-lists.sql). AllergyRecorded when an active allergy is why it was refused.
    public Task<ChartSave> RecordNoKnownAllergiesAsync(Guid patient, CancellationToken cancellationToken) =>
        SaveAsync(new AllergyIntolerance
        {
            Id = Guid.CreateVersion7(),
            PatientId = patient,
            Code = AllergyIntolerance.NoKnownAllergy,
            Display = "No known allergy",
            ClinicalStatus = AllergyClinicalStatus.Active,
            RecordedAt = DateTimeOffset.UtcNow,
        }, isNew: true, cancellationToken);

    // An insert, or an update of the named columns of the row with the entry's ID. Reads nothing: the page read the
    // entry as this patient's through PhiReader, and row-level security decides whether the write may happen.
    async Task<ChartSave> SaveAsync(object entry, bool isNew, CancellationToken cancellationToken, params string[] changed)
    {
        const string savepoint = "chart_entry";
        var transaction = db.Database.CurrentTransaction!;
        await transaction.CreateSavepointAsync(savepoint, cancellationToken);
        var tracked = isNew ? db.Add(entry) : db.Attach(entry);
        if (!isNew)
        {
            foreach (var property in changed)
            {
                tracked.Property(property).IsModified = true;
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.ReleaseSavepointAsync(savepoint, cancellationToken);
            return ChartSave.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            // No row: not an entry this clinician may change.
            await transaction.RollbackToSavepointAsync(savepoint, cancellationToken);
            return ChartSave.Refused;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege })
        {
            // Refused by row-level security.
            await transaction.RollbackToSavepointAsync(savepoint, cancellationToken);
            return ChartSave.Refused;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation }
            && entry is AllergyIntolerance { Code: AllergyIntolerance.NoKnownAllergy })
        {
            // "No known allergies" only: refused because the patient has an allergy recorded. Any other broken
            // constraint is a fault, and is thrown.
            await transaction.RollbackToSavepointAsync(savepoint, cancellationToken);
            return ChartSave.AllergyRecorded;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: AllergyIntoleranceConfiguration.OneNoKnownAllergyIndex,
            }
            && entry is AllergyIntolerance { Code: AllergyIntolerance.NoKnownAllergy })
        {
            // "No known allergies" is already recorded (a second click, or another tab): nothing more to save.
            await transaction.RollbackToSavepointAsync(savepoint, cancellationToken);
            return ChartSave.AlreadyRecorded;
        }
        finally
        {
            tracked.State = EntityState.Detached;
        }
    }

    // What was typed, with LIKE's own characters taken literally.
    static string Like(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);
}

// What saving a chart entry did. Nothing was saved unless Saved.
public enum ChartSave
{
    Saved,

    // The database refused the write: the patient isn't on the clinician's panel, or the entry isn't theirs.
    Refused,

    // "No known allergies" only: refused because the patient has an active allergy recorded.
    AllergyRecorded,

    // "No known allergies" only: it is already recorded, so nothing more was saved. As good as Saved to the clinician.
    AlreadyRecorded,
}
