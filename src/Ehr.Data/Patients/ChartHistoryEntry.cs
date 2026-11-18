using Ehr.Data.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ehr.Data.Patients;

// One change to a patient's allergies, medications or diagnoses: who made it, when, and the entry before and after.
// PHI: row-level security is enabled and forced on it; clinicians read it through the PHI reader. Nothing at runtime
// writes it but the database's own trigger on those three tables (db/policies/65-chart-lists.sql), so the history
// can't be skipped or edited. Synthetic data only.
public class ChartHistoryEntry
{
    public Guid Id { get; set; }

    public Guid PatientId { get; set; }

    // The table the entry is in, and the entry.
    public required string TableName { get; set; }

    public Guid RowId { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    // The signed-in staff account; null for a change made outside the app, such as the synthetic seed's.
    public Guid? ChangedById { get; set; }

    // The entry before the change, as JSON; null when it was added.
    public string? Before { get; set; }

    // The entry after the change, as JSON.
    public required string After { get; set; }
}

// Maps ehr.chart_history. EhrDbContext picks this up with ApplyConfigurationsFromAssembly.
public sealed class ChartHistoryEntryConfiguration : IEntityTypeConfiguration<ChartHistoryEntry>
{
    public void Configure(EntityTypeBuilder<ChartHistoryEntry> entry)
    {
        entry.ToTable("chart_history");
        entry.Property(e => e.TableName).HasMaxLength(63);
        entry.Property(e => e.Before).HasColumnType("jsonb");
        entry.Property(e => e.After).HasColumnType("jsonb");
        entry.HasOne<Patient>().WithMany().HasForeignKey(e => e.PatientId).OnDelete(DeleteBehavior.Restrict);
        entry.HasOne<StaffUser>().WithMany().HasForeignKey(e => e.ChangedById).OnDelete(DeleteBehavior.Restrict);
        // Read by patient and list, newest first.
        entry.HasIndex(e => new { e.PatientId, e.TableName, e.ChangedAt });
    }
}
