using InSalute.Core.Dispatching;
using InSalute.Core.Email;
using InSalute.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    /// <summary>
    /// Protezione degli allegati, invio e pulizia. Richiede <c>AddInSaluteSettings</c> e le opzioni
    /// <see cref="StorageOptions"/> e <see cref="RetentionOptions"/> (le registra l'host dalla configurazione).
    /// </summary>
    public static IServiceCollection AddInSaluteDispatching(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<TempFileStore>();
        services.AddSingleton<AttachmentProtector>();
        services.AddScoped<DispatchService>();
        services.AddScoped<RecipientChecks>();
        services.AddScoped<DispatchHistoryService>();
        services.AddScoped<DispatchCleanupService>();
        return services;
    }
}
