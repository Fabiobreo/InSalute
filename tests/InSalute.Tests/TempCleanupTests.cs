using InSalute.Core.Data;
using InSalute.Core.Dispatching;
using InSalute.Core.Domain;
using InSalute.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InSalute.Tests;

public class TempFileStoreTests : IAsyncLifetime
{
    private readonly TestEnvironment _env = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _env.DisposeAsync();

    [Fact]
    public async Task An_upload_that_stops_sending_data_times_out_and_leaves_no_file()
    {
        _env.Storage.UploadIdleTimeout = TimeSpan.FromMilliseconds(200);
        var store = _env.CreateScope().ServiceProvider.GetRequiredService<TempFileStore>();
        var draft = Guid.NewGuid();

        // Lo stream non risponde e ignora anche la cancellazione, come una connessione col browser caduta.
        await using var stream = new StalledStream([1, 2, 3]);
        await Assert.ThrowsAsync<TimeoutException>(() => store.SaveAsync(draft, "referto.pdf", stream));

        Assert.Empty(Directory.EnumerateFiles(store.GetDraftFolder(draft)));
    }

    [Fact]
    public async Task A_slow_upload_that_keeps_sending_data_is_not_cut_off()
    {
        _env.Storage.UploadIdleTimeout = TimeSpan.FromMilliseconds(200);
        var store = _env.CreateScope().ServiceProvider.GetRequiredService<TempFileStore>();

        // In tutto ci mette più del tempo massimo, ma ogni blocco arriva prima.
        await using var stream = new TricklingStream(chunks: 8, delay: TimeSpan.FromMilliseconds(60));
        var stored = await store.SaveAsync(Guid.NewGuid(), "referto.txt", stream);

        Assert.Equal(8, stored.SizeBytes);
    }

    [Fact]
    public async Task Cancelling_an_upload_is_not_reported_as_a_timeout()
    {
        var store = _env.CreateScope().ServiceProvider.GetRequiredService<TempFileStore>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await using var stream = new StalledStream([]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(Guid.NewGuid(), "referto.pdf", stream, cts.Token));
    }

    [Fact]
    public async Task Saved_file_has_a_guid_name_and_sha256_and_keeps_the_original_name_in_memory_only()
    {
        var store = _env.CreateScope().ServiceProvider.GetRequiredService<TempFileStore>();
        var draft = Guid.NewGuid();
        var content = TestFiles.Pdf(1);

        await using var stream = new MemoryStream(content);
        var stored = await store.SaveAsync(draft, "Referto Rossi.PDF", stream);

        Assert.Equal("Referto Rossi.PDF", stored.OriginalFileName);
        Assert.Matches("^[0-9a-f]{32}\\.pdf$", stored.StoredName);
        Assert.Equal(content.Length, stored.SizeBytes);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant(), stored.Sha256);

        var path = store.ResolveFile(draft, stored.StoredName);
        Assert.NotNull(path);
        Assert.Equal(store.GetDraftFolder(draft), Path.GetDirectoryName(path));
        Assert.DoesNotContain("Rossi", path);
    }

    [Theory]
    [InlineData("../x.pdf")]
    [InlineData("..\\x.pdf")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("referto.pdf")]
    public async Task Names_not_generated_by_the_store_are_never_resolved(string storedName)
    {
        var store = _env.CreateScope().ServiceProvider.GetRequiredService<TempFileStore>();
        var draft = Guid.NewGuid();
        await store.SaveAsync(draft, "a.pdf", new MemoryStream([1, 2, 3]));

        Assert.Null(store.ResolveFile(draft, storedName));
    }

    [Fact]
    public async Task DeleteDraft_removes_the_whole_folder()
    {
        var store = _env.CreateScope().ServiceProvider.GetRequiredService<TempFileStore>();
        var draft = Guid.NewGuid();
        await store.SaveAsync(draft, "a.pdf", new MemoryStream([1]));

        store.DeleteDraft(draft);

        Assert.False(Directory.Exists(store.GetDraftFolder(draft)));
    }
}

public class TempCleanupTests : IAsyncLifetime
{
    private readonly TestEnvironment _env = new();
    private ApplicationUser _operator = default!;

    public async Task InitializeAsync()
    {
        _operator = await _env.AddUserAsync("operatore", Roles.Operatore);
        _env.Clock.Now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    }

    public async Task DisposeAsync() => await _env.DisposeAsync();

    private string MakeFolder(TimeSpan age)
    {
        var folder = Path.Combine(_env.TempPath, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "x.tmp"), "x");
        Directory.SetLastWriteTimeUtc(folder, _env.Clock.Now.UtcDateTime - age);
        return folder;
    }

    private async Task<Guid> AddDispatchAsync(TimeSpan age, DispatchStepStatus report, DispatchStepStatus password)
    {
        using var scope = _env.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        db.Dispatches.Add(new Dispatch
        {
            Id = id,
            CreatedAt = _env.Clock.Now.UtcDateTime - age,
            CreatedByUserId = _operator.Id,
            PatientFirstName = "Mario",
            PatientLastName = "Rossi",
            RecipientEmail = "mario@example.test",
            ReportStatus = report,
            PasswordStatus = password,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Dispatch?> LoadAsync(Guid id)
    {
        using var scope = _env.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Dispatches.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
    }

    private async Task<CleanupSummary> RunCleanupAsync()
    {
        using var scope = _env.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DispatchCleanupService>().RunAsync();
    }

    [Fact]
    public async Task Folders_older_than_60_minutes_are_deleted_and_newer_ones_kept()
    {
        var old = MakeFolder(TimeSpan.FromMinutes(61));
        var recent = MakeFolder(TimeSpan.FromMinutes(59));

        var summary = await RunCleanupAsync();

        Assert.Equal(1, summary.FoldersDeleted);
        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(recent));
    }

    [Fact]
    public async Task Old_pending_dispatch_becomes_abandoned()
    {
        var id = await AddDispatchAsync(TimeSpan.FromMinutes(61), DispatchStepStatus.Pending, DispatchStepStatus.Pending);

        var summary = await RunCleanupAsync();

        var dispatch = await LoadAsync(id);
        Assert.Equal(1, summary.DispatchesAbandoned);
        Assert.Equal(DispatchStepStatus.Abandoned, dispatch!.ReportStatus);
        Assert.Equal(DispatchStepStatus.Abandoned, dispatch.PasswordStatus);
    }

    [Fact]
    public async Task Old_dispatch_with_report_sent_and_password_failed_abandons_only_the_password()
    {
        var id = await AddDispatchAsync(TimeSpan.FromHours(3), DispatchStepStatus.Sent, DispatchStepStatus.Failed);

        await RunCleanupAsync();

        var dispatch = await LoadAsync(id);
        Assert.Equal(DispatchStepStatus.Sent, dispatch!.ReportStatus);
        Assert.Equal(DispatchStepStatus.Abandoned, dispatch.PasswordStatus);
    }

    [Fact]
    public async Task Recent_pending_and_completed_dispatches_are_left_alone()
    {
        var recent = await AddDispatchAsync(TimeSpan.FromMinutes(10), DispatchStepStatus.Pending, DispatchStepStatus.Pending);
        var done = await AddDispatchAsync(TimeSpan.FromDays(2), DispatchStepStatus.Sent, DispatchStepStatus.Sent);
        var imported = await AddDispatchAsync(TimeSpan.FromDays(400), DispatchStepStatus.Unknown, DispatchStepStatus.Unknown);

        var summary = await RunCleanupAsync();

        Assert.Equal(0, summary.DispatchesAbandoned);
        Assert.Equal(DispatchStepStatus.Pending, (await LoadAsync(recent))!.ReportStatus);
        Assert.Equal(DispatchStepStatus.Sent, (await LoadAsync(done))!.PasswordStatus);
        Assert.Equal(DispatchStepStatus.Unknown, (await LoadAsync(imported))!.ReportStatus);
    }

    [Fact]
    public async Task History_is_never_deleted_by_default()
    {
        var old = await AddDispatchAsync(TimeSpan.FromDays(3650), DispatchStepStatus.Sent, DispatchStepStatus.Sent);

        var summary = await RunCleanupAsync();

        Assert.Equal(0, summary.DispatchesDeleted);
        Assert.NotNull(await LoadAsync(old));
    }

    [Fact]
    public async Task History_older_than_the_configured_years_is_deleted()
    {
        _env.Retention.DispatchYears = 2;
        var old = await AddDispatchAsync(TimeSpan.FromDays(800), DispatchStepStatus.Sent, DispatchStepStatus.Sent);
        var recent = await AddDispatchAsync(TimeSpan.FromDays(100), DispatchStepStatus.Sent, DispatchStepStatus.Sent);

        var summary = await RunCleanupAsync();

        Assert.Equal(1, summary.DispatchesDeleted);
        Assert.Null(await LoadAsync(old));
        Assert.NotNull(await LoadAsync(recent));
    }

    [Fact]
    public async Task Hosted_service_run_does_the_cleanup_and_never_throws()
    {
        var old = MakeFolder(TimeSpan.FromHours(2));
        var service = new TempCleanupHostedService(
            _env.CreateScope().ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TempCleanupHostedService>.Instance);

        await service.RunOnceAsync(CancellationToken.None);

        Assert.False(Directory.Exists(old));
    }
}
