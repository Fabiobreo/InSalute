using InSalute.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InSalute.Core.Data.Configurations;

public class DispatchConfiguration : IEntityTypeConfiguration<Dispatch>
{
    public void Configure(EntityTypeBuilder<Dispatch> builder)
    {
        // L'Id viene dal codice (è il DraftId della procedura guidata), mai dal database.
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.HasOne(d => d.CreatedBy).WithMany().HasForeignKey(d => d.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.PatientFirstName).HasMaxLength(100).IsRequired();
        builder.Property(d => d.PatientLastName).HasMaxLength(100).IsRequired();
        builder.Property(d => d.RecipientEmail).HasMaxLength(254).IsRequired();
        builder.Property(d => d.AttachmentsJson).IsRequired();
        builder.Property(d => d.InvoiceFileName).HasMaxLength(260);
        builder.Property(d => d.ReportError).HasMaxLength(1000);
        builder.Property(d => d.PasswordError).HasMaxLength(1000);
        builder.HasIndex(d => d.RecipientEmail);
        builder.HasIndex(d => d.CreatedAt);
    }
}
