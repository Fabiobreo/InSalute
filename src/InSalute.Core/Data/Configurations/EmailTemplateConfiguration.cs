using InSalute.Core.Domain;
using InSalute.Core.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InSalute.Core.Data.Configurations;

public class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    private static readonly DateTime SeedDate = new(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<EmailTemplate> builder)
    {
        builder.Property(t => t.Subject).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Body).IsRequired();
        builder.Property(t => t.UpdatedByUserId).HasMaxLength(450);
        builder.HasIndex(t => t.Kind).IsUnique();

        builder.HasData(
            new EmailTemplate
            {
                Id = 1,
                Kind = EmailTemplateKind.Report,
                Subject = EmailTemplateDefaults.ReportSubject,
                Body = EmailTemplateDefaults.ReportBody,
                UpdatedAt = SeedDate,
            },
            new EmailTemplate
            {
                Id = 2,
                Kind = EmailTemplateKind.Password,
                Subject = EmailTemplateDefaults.PasswordSubject,
                Body = EmailTemplateDefaults.PasswordBody,
                UpdatedAt = SeedDate,
            });
    }
}
