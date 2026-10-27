using InSalute.Core.Data;
using InSalute.Core.Domain;
using InSalute.LegacyImport;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InSalute.Tests;

public sealed class LegacyImporterTests : IAsyncLifetime
{
    private readonly TestEnvironment _env = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _env.DisposeAsync();

    private static readonly DateTime T0 = new(2025, 3, 10, 10, 0, 0);

    private static LegacyData Sample() => new(
        [
            new LegacyUser(1, "capo@example.test", "capo", new DateTime(2024, 1, 15, 9, 0, 0), "admin"),
            new LegacyUser(2, "man@example.test", "man", new DateTime(2024, 1, 16, 9, 0, 0), "manager"),
            new LegacyUser(3, "op@example.test", "op", new DateTime(2024, 1, 17, 9, 0, 0), "user"),
            new LegacyUser(4, "strano@example.test", "strano", new DateTime(2024, 1, 17, 9, 0, 0), "boh"),
        ],
        [
            // Un invio completo: due email a pochi secondi.
            new LegacyLog(10, 3, "Paz@Example.test ", T0),
            new LegacyLog(11, 3, "paz@example.test", T0.AddSeconds(8)),
            // Stesso destinatario, un'ora dopo: altro invio.
            new LegacyLog(12, 3, "paz@example.test", T0.AddHours(1)),
            // Altro utente, stesso destinatario e orario: altro invio.
            new LegacyLog(13, 2, "paz@example.test", T0),
            // Utente non importabile: riga scartata.
            new LegacyLog(14, 4, "x@example.test", T0),
            // Utente sconosciuto: riga scartata.
            new LegacyLog(15, 99, "x@example.test", T0),
        ]);

    private async Task<ImportSummary> RunAsync(LegacyData data, bool dryRun)
    {
        using var scope = _env.CreateScope();
        var importer = ActivatorUtilities.CreateInstance<LegacyImporter>(scope.ServiceProvider);
        return await importer.ImportAsync(data, dryRun);
    }

    private async Task<(List<ApplicationUser> Users, List<Dispatch> Dispatches)> StateAsync()
    {
        using var scope = _env.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Users.AsNoTracking().ToListAsync(), await db.Dispatches.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Dry_run_reports_counts_and_writes_nothing()
    {
        var summary = await RunAsync(Sample(), dryRun: true);

        Assert.Equal(4, summary.UsersRead);
        Assert.Equal(3, summary.UsersToCreate);
        Assert.Equal(1, summary.UsersSkipped);
        Assert.Equal(6, summary.LogRows);
        Assert.Equal(2, summary.LogRowsSkipped);
        Assert.Equal(3, summary.DispatchesResulting);
        Assert.Equal(3, summary.DispatchesToCreate);

        var (users, dispatches) = await StateAsync();
        Assert.Empty(users);
        Assert.Empty(dispatches);
    }

    [Fact]
    public async Task Import_creates_inactive_users_without_password_and_groups_the_log_rows()
    {
        var summary = await RunAsync(Sample(), dryRun: false);

        Assert.Equal(3, summary.UsersToCreate);
        Assert.Equal(3, summary.DispatchesToCreate);

        var (users, dispatches) = await StateAsync();
        Assert.Equal(3, users.Count);
        Assert.All(users, u =>
        {
            Assert.False(u.IsActive);
            Assert.True(u.MustChangePassword);
            Assert.Null(u.PasswordHash);
            Assert.NotNull(u.LegacyId);
        });
        var op = users.Single(u => u.LegacyId == 3);
        Assert.Equal("op", op.UserName);
        // 17/01/2024 09:00 ora italiana (CET) = 08:00 UTC.
        Assert.Equal(new DateTime(2024, 1, 17, 8, 0, 0), op.CreatedAt);

        Assert.Equal(3, dispatches.Count);
        Assert.All(dispatches, d =>
        {
            Assert.True(d.IsLegacyImport);
            Assert.Equal("", d.PatientFirstName);
            Assert.Equal("", d.PatientLastName);
            Assert.Equal("[]", d.AttachmentsJson);
            Assert.Equal(ProtectionKind.Unknown, d.ProtectionKind);
            Assert.Equal(DispatchStepStatus.Unknown, d.ReportStatus);
            Assert.Equal(DispatchStepStatus.Unknown, d.PasswordStatus);
        });
        var grouped = dispatches.Where(d => d.CreatedByUserId == op.Id).OrderBy(d => d.CreatedAt).ToList();
        Assert.Equal(2, grouped.Count);
        Assert.Equal("paz@example.test", grouped[0].RecipientEmail);
        // 10:00 ora italiana di marzo (CET) = 09:00 UTC.
        Assert.Equal(new DateTime(2025, 3, 10, 9, 0, 0), grouped[0].CreatedAt);

        using var scope = _env.CreateScope();
        var users2 = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Contains(Roles.Operatore, await users2.GetRolesAsync(op));
        Assert.Contains(Roles.Admin, await users2.GetRolesAsync(users.Single(u => u.LegacyId == 1)));
    }

    [Fact]
    public async Task Running_it_twice_adds_nothing()
    {
        await RunAsync(Sample(), dryRun: false);
        var before = await StateAsync();

        var second = await RunAsync(Sample(), dryRun: false);

        Assert.Equal(0, second.UsersToCreate);
        Assert.Equal(3, second.UsersAlreadyImported);
        Assert.Equal(0, second.DispatchesToCreate);
        Assert.Equal(3, second.DispatchesAlreadyImported);
        var after = await StateAsync();
        Assert.Equal(before.Users.Count, after.Users.Count);
        Assert.Equal(before.Dispatches.Count, after.Dispatches.Count);
    }

    [Fact]
    public async Task A_duplicate_email_in_the_old_database_is_skipped_not_fatal()
    {
        var data = new LegacyData(
            [
                new LegacyUser(1, "stessa@example.test", "uno", T0, "user"),
                new LegacyUser(2, "stessa@example.test", "due", T0, "user"),
            ],
            []);

        var summary = await RunAsync(data, dryRun: false);

        Assert.Equal(1, summary.UsersToCreate);
        Assert.Equal(1, summary.UsersSkipped);
        Assert.Single((await StateAsync()).Users);
    }

    [Fact]
    public void Dispatch_ids_are_stable_and_distinct()
    {
        Assert.Equal(LegacyImporter.DispatchIdFor(10), LegacyImporter.DispatchIdFor(10));
        Assert.NotEqual(LegacyImporter.DispatchIdFor(10), LegacyImporter.DispatchIdFor(11));
    }

    [Fact]
    public void A_non_existent_local_time_during_the_dst_jump_is_shifted_forward()
    {
        // 30/03/2025 02:30 non esiste in Italia (si passa da 02:00 a 03:00).
        var utc = LegacyImporter.ToUtc(new DateTime(2025, 3, 30, 2, 30, 0));

        Assert.Equal(new DateTime(2025, 3, 30, 1, 30, 0), utc);
    }
}
