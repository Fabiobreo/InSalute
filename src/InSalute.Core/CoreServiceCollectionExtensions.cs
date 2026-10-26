using InSalute.Core.Email;
using InSalute.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace InSalute.Core;

public static class CoreServiceCollectionExtensions
{
    /// <summary>Impostazioni SMTP, modelli email e invio con MailKit. Richiede Data Protection e Identity (<c>AddInSaluteIdentity</c>).</summary>
    public static IServiceCollection AddInSaluteSettings(this IServiceCollection services)
    {
        services.AddScoped<IEmailSender, MailKitEmailSender>();
        services.AddScoped<SmtpSettingsService>();
        services.AddScoped<EmailTemplateService>();
        return services;
    }
}
