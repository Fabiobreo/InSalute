using InSalute.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InSalute.Core.Data.Configurations;

public class SmtpSettingsConfiguration : IEntityTypeConfiguration<SmtpSettings>
{
    public void Configure(EntityTypeBuilder<SmtpSettings> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint("CK_SmtpSettings_SingleRow", "[Id] = 1"));
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Host).HasMaxLength(255).IsRequired();
        builder.Property(s => s.UserName).HasMaxLength(254).IsRequired();
        builder.Property(s => s.ProtectedPassword).IsRequired();
        builder.Property(s => s.FromAddress).HasMaxLength(254).IsRequired();
        builder.Property(s => s.FromName).HasMaxLength(100).IsRequired();
        builder.Property(s => s.UpdatedByUserId).HasMaxLength(450);
    }
}
