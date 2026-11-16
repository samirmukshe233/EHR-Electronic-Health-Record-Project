using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ehr.Data.Patients;

// Every review mutation serializes with signing on the source draft. Only PhiReader calls this; it audits the row read.
public static class ClinicalDraftLock
{
    public static async Task<bool> AcquireAsync(EhrDbContext db, Guid id, Guid patient, CancellationToken cancel)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Review mutations require a database transaction.");
        return await db.Database.GetDbConnection().ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT 1 FROM ehr.clinical_drafts WHERE id = @id AND patient_id = @patient FOR UPDATE",
            new { id, patient }, transaction.GetDbTransaction(), cancellationToken: cancel)) is not null;
    }
}
