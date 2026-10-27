using InSalute.Core.Data;
using InSalute.Core.Users;
using InSalute.LegacyImport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Uso: InSalute.LegacyImport --source "<connection string vecchio DB>" --target "<connection string InSaluteV2>" [--dry-run]
// Stampa solo conteggi: mai email, nomi o connection string.

static string? Arg(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

var source = Arg(args, "--source");
var target = Arg(args, "--target");
var dryRun = args.Contains("--dry-run");
if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
{
    Console.Error.WriteLine("Uso: InSalute.LegacyImport --source \"<vecchio DB>\" --target \"<InSaluteV2>\" [--dry-run]");
    return 2;
}

try
{
    var data = await LegacyReader.ReadAsync(source);

    var services = new ServiceCollection();
    services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
    // Richiesto dall'infrastruttura di Identity; i token non vengono usati.
    services.AddDataProtection();
    services.AddDbContext<AppDbContext>(o => o.UseSqlServer(target));
    services.AddInSaluteIdentity();
    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();

    var importer = ActivatorUtilities.CreateInstance<LegacyImporter>(scope.ServiceProvider);
    var summary = await importer.ImportAsync(data, dryRun);

    var created = dryRun ? "da creare" : "creati";
    Console.WriteLine(dryRun ? "SIMULAZIONE (nessuna scrittura)" : "IMPORT ESEGUITO");
    Console.WriteLine($"Utenti letti:        {summary.UsersRead}");
    Console.WriteLine($"  già importati:     {summary.UsersAlreadyImported}");
    Console.WriteLine($"  {created + ":",-17} {summary.UsersToCreate}");
    Console.WriteLine($"  non importabili:   {summary.UsersSkipped}");
    Console.WriteLine($"Righe di log lette:  {summary.LogRows}");
    Console.WriteLine($"  scartate:          {summary.LogRowsSkipped}");
    Console.WriteLine($"Invii risultanti:    {summary.DispatchesResulting}");
    Console.WriteLine($"  già importati:     {summary.DispatchesAlreadyImported}");
    Console.WriteLine($"  {created + ":",-17} {summary.DispatchesToCreate}");
    return 0;
}
catch (Exception ex)
{
    // Il messaggio può contenere nomi di server o di database: si stampa solo il tipo.
    Console.Error.WriteLine($"Import non riuscito ({ex.GetType().Name}).");
    return 1;
}
