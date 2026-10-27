using InSalute.Core.Dispatching;
using InSalute.Core.Domain;
using InSalute.Core.Settings;
using InSalute.Core.Texts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InSalute.Tests;

/// <summary>Piccole aggiunte a Core per la procedura guidata (Fase 5): ispezione PDF, cancellazione di un file, anteprima email.</summary>
public class CoreAdditionsTests : IAsyncLifetime
{
    private readonly TestEnvironment _env = new();

    public async Task InitializeAsync()
    {
        var admin = await _env.AddUserAsync("admin", Roles.Admin);
        _env.CurrentUser.UserId = admin.Id;
        using var scope = _env.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<SmtpSettingsService>().SaveAsync(
            new SmtpSettingsInput("localhost", 25, SmtpSecurity.StartTls, "u@example.test", "segreta", "info@example.test", "Centro"));
        Assert.True(saved.Succeeded);
        var op = await _env.AddUserAsync("operatore", Roles.Operatore);
        _env.CurrentUser.UserId = op.Id;
    }

    public async Task DisposeAsync() => await _env.DisposeAsync();

    private TempFileStore Store => _env.CreateScope().ServiceProvider.GetRequiredService<TempFileStore>();

    [Fact]
    public async Task InspectPdf_returns_the_page_count()
    {
        var draft = Guid.NewGuid();
        var stored = await Store.SaveAsync(draft, "a.pdf", new MemoryStream(TestFiles.Pdf(3)));

        var pages = await new AttachmentProtector().InspectPdfAsync(Store.ResolveFile(draft, stored.StoredName)!, "a.pdf");

        Assert.Equal(3, pages);
    }

    [Fact]
    public async Task InspectPdf_rejects_an_already_protected_pdf_and_a_corrupt_one()
    {
        var draft = Guid.NewGuid();
        var protectedPdf = await Store.SaveAsync(draft, "p.pdf", new MemoryStream(TestFiles.ProtectedPdf("segreta123")));
        var corrupt = await Store.SaveAsync(draft, "c.pdf", new MemoryStream([1, 2, 3, 4]));
        var protector = new AttachmentProtector();

        await Assert.ThrowsAsync<ProtectedPdfException>(() =>
            protector.InspectPdfAsync(Store.ResolveFile(draft, protectedPdf.StoredName)!, "p.pdf"));
        await Assert.ThrowsAsync<InvalidPdfException>(() =>
            protector.InspectPdfAsync(Store.ResolveFile(draft, corrupt.StoredName)!, "c.pdf"));
    }

    [Fact]
    public void KindFor_is_case_insensitive_on_the_extension()
    {
        Assert.Equal(ProtectionKind.PdfAes, AttachmentProtector.KindFor(["a.pdf", "B.PDF"]));
        Assert.Equal(ProtectionKind.ZipAes, AttachmentProtector.KindFor(["a.pdf", "foto.JPG"]));
    }

    [Fact]
    public async Task DeleteFile_removes_only_that_file_and_ignores_invalid_names()
    {
        var store = Store;
        var draft = Guid.NewGuid();
        var one = await store.SaveAsync(draft, "a.pdf", new MemoryStream(TestFiles.Pdf(1)));
        var two = await store.SaveAsync(draft, "b.pdf", new MemoryStream(TestFiles.Pdf(1)));

        store.DeleteFile(draft, one.StoredName);
        store.DeleteFile(draft, "../../fuori.txt");

        Assert.Null(store.ResolveFile(draft, one.StoredName));
        Assert.NotNull(store.ResolveFile(draft, two.StoredName));
    }

    [Fact]
    public async Task Preview_renders_both_emails_with_a_masked_password_for_an_operator()
    {
        using var scope = _env.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<DispatchService>()
            .PreviewAsync("Mario", "Rossi", ["a.pdf", "b.pdf"]);

        Assert.True(result.Succeeded, result.Error);
        var preview = result.Value!;
        Assert.Equal(ProtectionKind.PdfAes, preview.ProtectionKind);
        Assert.StartsWith("Referto_Rossi_Mario_", preview.ProtectedFileName);
        Assert.EndsWith(".pdf", preview.ProtectedFileName);
        Assert.Contains("Mario Rossi", preview.ReportBody);
        Assert.DoesNotContain("7-Zip", preview.ReportBody);
        Assert.Contains(DispatchService.PasswordMask, preview.PasswordBody);
        Assert.DoesNotContain("{", preview.PasswordBody);
    }

    [Fact]
    public async Task Preview_of_a_zip_adds_the_7zip_note_and_uses_the_zip_extension()
    {
        using var scope = _env.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<DispatchService>()
            .PreviewAsync("Mario", "Rossi", ["a.pdf", "foto.jpg"]);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(ProtectionKind.ZipAes, result.Value!.ProtectionKind);
        Assert.EndsWith(".zip", result.Value.ProtectedFileName);
        Assert.Contains(Messaggi.NotaArchivioZip, result.Value.ReportBody);
    }

    [Fact]
    public async Task Preview_is_refused_without_a_logged_in_user()
    {
        _env.CurrentUser.UserId = null;
        using var scope = _env.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<DispatchService>()
            .PreviewAsync("Mario", "Rossi", ["a.pdf"]);

        Assert.False(result.Succeeded);
    }
}
