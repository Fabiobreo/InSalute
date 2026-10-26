using InSalute.Core.Dispatching;

namespace InSalute.Web.Services;

/// <summary>Pulizia di file temporanei e invii rimasti a metà: subito all'avvio, poi ogni 15 minuti.</summary>
public sealed class TempCleanupHostedService(IServiceScopeFactory scopes, ILogger<TempCleanupHostedService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<DispatchCleanupService>().RunAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Un errore di pulizia non deve fermare l'app; nei log solo il tipo di errore, mai dati dei pazienti.
            logger.LogError(ex, "Pulizia dei file temporanei non riuscita");
        }
    }
}
