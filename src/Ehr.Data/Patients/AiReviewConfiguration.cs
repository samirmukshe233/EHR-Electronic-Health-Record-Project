using Ehr.Data.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ehr.Data.Patients;

public sealed class AiReviewConfiguration : IEntityTypeConfiguration<AiNoteReview>, IEntityTypeConfiguration<AiFieldProposal>
{
    public void Configure(EntityTypeBuilder<AiNoteReview> review)
    {
        review.HasOne<ClinicalDraft>().WithMany().HasForeignKey(r => r.DraftId).OnDelete(DeleteBehavior.Restrict);
        review.HasOne<Patient>().WithMany().HasForeignKey(r => r.PatientId).OnDelete(DeleteBehavior.Restrict);
        review.HasOne<StaffUser>().WithMany().HasForeignKey(r => r.ClinicianId).OnDelete(DeleteBehavior.Restrict);
        review.HasAlternateKey(r => new { r.Id, r.PatientId });
        review.HasIndex(r => r.SessionId).IsUnique();
        review.HasIndex(r => new { r.DraftId, r.State });
        review.Property(r => r.SessionId).HasMaxLength(64);
        review.Property(r => r.State).HasMaxLength(16).IsConcurrencyToken();
        review.Property(r => r.ResultJson).HasColumnType("jsonb");
        review.Property(r => r.CreatedAt).HasDefaultValueSql("now()");
        review.Property(r => r.CompletedAt).ValueGeneratedOnAddOrUpdate();
    }

    public void Configure(EntityTypeBuilder<AiFieldProposal> proposal)
    {
        proposal.HasOne<AiNoteReview>().WithMany().HasForeignKey(p => new { p.ReviewId, p.PatientId })
            .HasPrincipalKey(r => new { r.Id, r.PatientId }).OnDelete(DeleteBehavior.Restrict);
        proposal.HasOne<StaffUser>().WithMany().HasForeignKey(p => p.ReviewerId).OnDelete(DeleteBehavior.Restrict);
        proposal.HasIndex(p => new { p.ReviewId, p.Field }).IsUnique();
        proposal.HasIndex(p => p.DecisionKey).IsUnique();
        proposal.Property(p => p.Field).HasMaxLength(16);
        proposal.Property(p => p.State).HasMaxLength(16).IsConcurrencyToken();
        proposal.Property(p => p.ReviewerId).ValueGeneratedOnAddOrUpdate();
        proposal.Property(p => p.ReviewedAt).ValueGeneratedOnAddOrUpdate();
    }
}
