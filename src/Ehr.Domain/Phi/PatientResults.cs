using Ehr.Data.Patients;

namespace Ehr.Domain.Phi;

// A patient's released test results as PhiReader.ResultsAsync returns them, already recorded as one read of the
// patient, newest first (UC6; Simar Rekhi).
public sealed record PatientResults(Patient Patient, IReadOnlyList<Observation> Results);
