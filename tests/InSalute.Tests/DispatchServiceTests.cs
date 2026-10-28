using System.Text.Json;
using ICSharpCode.SharpZipLib.Zip;
using InSalute.Core.Data;
using InSalute.Core.Dispatching;
using InSalute.Core.Domain;
using InSalute.Core.Email;
using InSalute.Core.Settings;
using InSalute.Core.Texts;
using InSalute.Core.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InSalute.Tests;

public class DispatchServiceTests : IAsyncLifetime
{
    private readonly TestEnvironment _env = new();
    private ApplicationUser _operator = default!;

    public async Task InitializeAsync()
    {
        var admin = await _env.AddUserAsync("admin", Roles.Admin);
        _operator = await _env.AddUserAsync("operatore", Roles.Operatore);

        _env.CurrentUser.UserId = admin.Id;
        using (var scope = _env.CreateScope())
        {
            var saved = await scope.ServiceProvider.GetRequiredService<SmtpSettingsService>().SaveAsync(
                new SmtpSettingsInput("localhost", 25, SmtpSecurity.StartTls, "u@example.test", "segreta", "info@example.test", "Centro"));
            Assert.True(saved.Succeeded);
        }
        _env.CurrentUser.UserId = _operator.Id;
    }

    public async Task DisposeAsync() => await _env.DisposeAsync();

    private async Task<StoredFile> StoreAsync(Guid draft, string name, byte[] content)
    {
        using var scope = _env.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TempFileStore>().SaveAsync(draft, name, new MemoryStream(content));
    }

    private async Task<DispatchRequest> PdfRequestAsync(int pages = 2, bool withInvoice = false, string last = "Rossi")
    {
        var draft = Guid.NewGuid();
        var report = await StoreAsync(draft, "referto.pdf", TestFiles.Pdf(pages));
        var invoice = withInvoice ? await StoreAsync(draft, "fattura.pdf", TestFiles.Pdf(7)) : null;
        return new DispatchRequest(draft, "Mario", last, "mario.rossi@example.test", [report], invoice);
    }

    private async Task<T> WithServiceAsync<T>(Func<DispatchService, Task<T>> action)
    {
        using var scope = _env.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DispatchService>());
    }

    private async Task<PreparedDispatch> PrepareOkAsync(DispatchRequest request)
    {
        var result = await WithServiceAsync(s => s.PrepareAsync(request));
        Assert.True(result.Succeeded, result.Error);
        return result.Value!;
    }

    private Task<DispatchStepResult> SendReportAsync(PreparedDispatch p) => WithServiceAsync(s => s.SendReportAsync(p));

    private Task<DispatchStepResult> SendPasswordAsync(PreparedDispatch p) => WithServiceAsync(s => s.SendPasswordAsync(p));

    private async Task<Dispatch> StoredAsync(Guid id)
    {
        using var scope = _env.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Dispatches.AsNoTracking().SingleAsync(d => d.Id == id);
    }

    private string DraftFolder(Guid id) =>
        _env.CreateScope().ServiceProvider.GetRequiredService<TempFileStore>().GetDraftFolder(id);

    private static SendResult Failure(SendErrorCode code = SendErrorCode.ConnectionFailed, string? details = "boom") => SendResult.Fail(code, details);

    // ---- Prepare ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Prepare_saves_the_dispatch_as_pending_with_data_and_hashes()
    {
        var request = await PdfRequestAsync(withInvoice: true);

        var prepared = await PrepareOkAsync(request);

        var dispatch = await StoredAsync(prepared.DispatchId);
        Assert.Equal(request.DraftId, dispatch.Id);
        Assert.Equal(_operator.Id, dispatch.CreatedByUserId);
        Assert.Equal(("Mario", "Rossi"), (dispatch.PatientFirstName, dispatch.PatientLastName));
        Assert.Equal("mario.rossi@example.test", dispatch.RecipientEmail);
        Assert.Equal(DispatchStepStatus.Pending, dispatch.ReportStatus);
        Assert.Equal(DispatchStepStatus.Pending, dispatch.PasswordStatus);
        Assert.Equal(ProtectionKind.PdfAes, dispatch.ProtectionKind);
        Assert.Equal("fattura.pdf", dispatch.InvoiceFileName);
        Assert.False(dispatch.IsLegacyImport);

        var attachments = JsonSerializer.Deserialize<List<JsonElement>>(dispatch.AttachmentsJson)!;
        var single = Assert.Single(attachments);
        Assert.Equal("referto.pdf", single.GetProperty("fileName").GetString());
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(TestFiles.Pdf(2))).Length, single.GetProperty("sha256").GetString()!.Length);
        Assert.Equal(PasswordGenerator.Length, prepared.Password.Length);
        Assert.Matches("^Referto_Rossi_Mario_\\d{8}\\.pdf$", prepared.FileName);
    }

    [Fact]
    public async Task The_document_password_appears_in_no_field_of_the_saved_dispatch()
    {
        var prepared = await PrepareOkAsync(await PdfRequestAsync());
        await SendReportAsync(prepared);
        await SendPasswordAsync(prepared);

        var dispatch = await StoredAsync(prepared.DispatchId);

        var everything = JsonSerializer.Serialize(dispatch);
        Assert.DoesNotContain(prepared.Password, everything);
        Assert.Equal(DispatchStepStatus.Sent, dispatch.PasswordStatus);
    }

    [Fact]
    public async Task Prepare_without_smtp_settings_is_refused()
    {
        var request = await PdfRequestAsync();
        using (var scope = _env.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.SmtpSettings.ExecuteDeleteAsync();
        }

        var result = await WithServiceAsync(s => s.PrepareAsync(request));

        Assert.False(result.Succeeded);
        Assert.Equal(Messaggi.SmtpNonConfigurato, result.Error);
    }

    [Fact]
    public async Task Prepare_requires_a_logged_in_user_allowed_to_send()
    {
        var request = await PdfRequestAsync();
        _env.CurrentUser.UserId = null;

        var result = await WithServiceAsync(s => s.PrepareAsync(request));

        Assert.False(result.Succeeded);
        Assert.Equal(Messaggi.NonAutorizzato, result.Error);
    }

    [Fact]
    public async Task Prepare_is_refused_for_a_user_who_must_still_change_the_password()
    {
        var temp = await _env.AddUserAsync("nuovo", Roles.Operatore, mustChangePassword: true);
        var request = await PdfRequestAsync();
        _env.CurrentUser.UserId = temp.Id;

        var result = await WithServiceAsync(s => s.PrepareAsync(request));

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("", "Mario", "mario@example.test", "Inserisci il cognome.")]
    [InlineData("Rossi", " ", "mario@example.test", "Inserisci il nome.")]
    [InlineData("Rossi", "Mario", "mario@example", "L'indirizzo email non è valido.")]
    [InlineData("Rossi", "Mario", "Mario <mario@example.test>", "L'indirizzo email non è valido.")]
    public async Task Prepare_validates_the_patient_data_again(string last, string first, string email, string error)
    {
        var request = (await PdfRequestAsync()) with { LastName = last, FirstName = first, RecipientEmail = email };

        var result = await WithServiceAsync(s => s.PrepareAsync(request));

        Assert.False(result.Succeeded);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public async Task Prepare_refuses_disallowed_extensions_too_many_files_and_a_missing_report()
    {
        var draft = Guid.NewGuid();
        var exe = await StoreAsync(draft, "virus.exe", [1, 2, 3]);
        var pdf = await StoreAsync(draft, "a.pdf", TestFiles.Pdf(1));

        var bad = await WithServiceAsync(s => s.PrepareAsync(new DispatchRequest(draft, "Mario", "Rossi", "m@example.test", [pdf, exe], null)));
        var none = await WithServiceAsync(s => s.PrepareAsync(new DispatchRequest(draft, "Mario", "Rossi", "m@example.test", [], null)));
        var many = await WithServiceAsync(s => s.PrepareAsync(new DispatchRequest(
            draft, "Mario", "Rossi", "m@example.test", Enumerable.Repeat(pdf, 11).ToList(), null)));
        var jpg = await StoreAsync(draft, "fattura.jpg", TestFiles.Image());
        var invoiceNotPdf = await WithServiceAsync(s => s.PrepareAsync(new DispatchRequest(
            draft, "Mario", "Rossi", "m@example.test", [pdf], jpg)));

        Assert.Equal(Messaggi.TipoFileNonAmmesso("virus.exe"), bad.Error);
        Assert.Equal(Messaggi.NessunReferto, none.Error);
        Assert.Equal(Messaggi.TroppiReferti, many.Error);
        Assert.Equal(Messaggi.FatturaNonPdf, invoiceNotPdf.Error);
    }

    [Fact]
    public async Task Prepare_never_trusts_stored_names_coming_from_the_ui()
    {
        var request = await PdfRequestAsync();
        var forged = request with { Reports = [new StoredFile("..\\..\\windows\\win.ini", "referto.pdf", 1, "")] };

        var result = await WithServiceAsync(s => s.PrepareAsync(forged));

        Assert.False(result.Succeeded);
        Assert.Equal(Messaggi.FileNonDisponibile, result.Error);
    }

    [Fact]
    public async Task Prepare_with_an_already_protected_pdf_fails_with_a_clear_message_and_saves_nothing()
    {
        var draft = Guid.NewGuid();
        var locked = await StoreAsync(draft, "chiuso.pdf", TestFiles.ProtectedPdf("segreta"));
        var before = Directory.GetFiles(DraftFolder(draft)).Length;

        var result = await WithServiceAsync(s => s.PrepareAsync(new DispatchRequest(draft, "Mario", "Rossi", "m@example.test", [locked], null)));

        Assert.False(result.Succeeded);
        Assert.Equal(Messaggi.PdfGiaProtetto, result.Error);
        Assert.Equal(before, Directory.GetFiles(DraftFolder(draft)).Length);
        using var scope = _env.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Dispatches.AnyAsync());
    }

    [Fact]
    public async Task Prepare_twice_for_the_same_draft_is_refused()
    {
        var request = await PdfRequestAsync();
        await PrepareOkAsync(request);

        var second = await WithServiceAsync(s => s.PrepareAsync(request));

        Assert.False(second.Succeeded);
        Assert.Equal(Messaggi.InvioGiaPreparato, second.Error);
    }

    [Fact]
    public async Task The_invoice_travels_unprotected_and_is_never_part_of_the_protected_package()
    {
        var invoiceBytes = TestFiles.Pdf(7);
        var prepared = await PrepareOkAsync(await PdfRequestAsync(pages: 2, withInvoice: true));

        await SendReportAsync(prepared);

        var attachments = _env.EmailSender.SentAttachments.Single();
        Assert.Equal(2, attachments.Count);
        var protectedFile = attachments.Single(a => a.FileName == prepared.FileName);
        var invoice = attachments.Single(a => a.FileName == "fattura.pdf");
        Assert.Equal(2, TestFiles.PageCount(protectedFile.Content, prepared.Password));
        Assert.Equal(7, TestFiles.PageCount(invoice.Content));
        Assert.Equal(invoiceBytes.Length, invoice.Content.Length);
    }

    [Fact]
    public async Task Non_pdf_reports_make_a_zip_and_the_first_email_explains_how_to_open_it()
    {
        var draft = Guid.NewGuid();
        var pdf = await StoreAsync(draft, "referto.pdf", TestFiles.Pdf(1));
        var jpg = await StoreAsync(draft, "radiografia.JPG", TestFiles.Image());
        var prepared = await PrepareOkAsync(new DispatchRequest(draft, "Mario", "Rossi", "m@example.test", [pdf, jpg], null));

        await SendReportAsync(prepared);

        Assert.Equal(ProtectionKind.ZipAes, prepared.ProtectionKind);
        Assert.EndsWith(".zip", prepared.FileName);
        var message = _env.EmailSender.Sent.Single();
        Assert.EndsWith(Messaggi.NotaArchivioZip, message.Body);
        Assert.Contains("allego il referto", message.Body);

        var zipBytes = _env.EmailSender.SentAttachments.Single().Single().Content;
        using var zip = new ZipFile(new MemoryStream(zipBytes)) { Password = prepared.Password };
        Assert.All(zip.Cast<ZipEntry>(), e => Assert.Equal(256, e.AESKeySize));
        Assert.NotNull(zip.GetEntry("radiografia.JPG"));
    }

    // ---- Regole dei due invii --------------------------------------------------------------------------------

    [Fact]
    public async Task Happy_path_sends_the_report_first_then_the_password_with_the_templates()
    {
        var prepared = await PrepareOkAsync(await PdfRequestAsync());

        var report = await SendReportAsync(prepared);
        var password = await SendPasswordAsync(prepared);

        Assert.True(report.Succeeded);
        Assert.True(password.Succeeded);
        Assert.Equal(2, _env.EmailSender.Sent.Count);

        var first = _env.EmailSender.Sent[0];
        Assert.Equal(EmailTemplateDefaults.ReportSubject, first.Subject);
        Assert.Contains("Buongiorno Mario Rossi,", first.Body);
        Assert.DoesNotContain(prepared.Password, first.Body);
        Assert.Equal("mario.rossi@example.test", first.ToAddress);
        Assert.Equal(prepared.FileName, first.Attachments!.Single().FileName);
        Assert.Equal(2, TestFiles.PageCount(_env.EmailSender.SentAttachments[0].Single().Content, prepared.Password));

        var second = _env.EmailSender.Sent[1];
        Assert.Equal(EmailTemplateDefaults.PasswordSubject, second.Subject);
        Assert.Contains(prepared.Password, second.Body);
        Assert.Null(second.Attachments);

        var dispatch = await StoredAsync(prepared.DispatchId);
        Assert.Equal((DispatchStepStatus.Sent, DispatchStepStatus.Sent), (dispatch.ReportStatus, dispatch.PasswordStatus));
        Assert.NotNull(dispatch.ReportSentAt);
        Assert.NotNull(dispatch.PasswordSentAt);
        Assert.Null(dispatch.ReportError);
    }

    [Fact]
    public async Task When_the_first_email_fails_no_password_email_is_sent_and_the_error_is_recorded()
    {
        var prepared = await PrepareOkAsync(await PdfRequestAsync());
        _env.EmailSender.FailWith = Failure(SendErrorCode.AuthenticationFailed, "535 auth");

        var report = await SendReportAsync(prepared);

        Assert.True(report.Attempted);
        Assert.False(report.Succeeded);
        Assert.Equal(Messaggi.PerErroreInvio(SendErrorCode.AuthenticationFailed), report.UserMessage);
        Assert.Empty(_env.EmailSender.Sent);

        var dispatch = await StoredAsync(prepared.DispatchId);
        Assert.Equal(DispatchStepStatus.Failed, dispatch.ReportStatus);
        Assert.Equal(DispatchStepStatus.Pending, dispatch.PasswordStatus);
        Assert.Null(dispatch.ReportSentAt);
        Assert.Contains("AuthenticationFailed", dispatch.ReportError);
    }

    [Fact]
    public async Task SendPassword_when_the_report_is_not_sent_throws_and_sends_nothing()
    {
        var prepared = await PrepareOkAsync(await PdfRequestAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() => SendPasswordAsync(prepared));

        _env.EmailSender.FailWith = Failure();
        await SendReportAsync(prepared);
        _env.EmailSender.FailWith = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => SendPasswordAsync(prepared));

        Assert.Empty(_env.EmailSender.Sent);
    }

    [Fact]
    public async Task The_state_checked_is_the_one_in_the_database_not_the_one_in_memory()
    {
        var prepared = await PrepareOkAsync(await PdfRequestAsync());
        // Un PreparedDispatch non può "dichiarare" il referto inviato: conta solo il database.
        await Assert.ThrowsAsync<InvalidOperationException>(() => SendPasswordAsync(prepared));
        Assert.DoesNotContain(typeof(PreparedDispatch).GetProperties(), p => p.PropertyType == typeof(DispatchStepStatus));
    }

    [Fact]
    public async Task Retry_after_a_failed_first_email_sends_the_report_and_then_the_password()
    {
        var prepared = await PrepareOkAsync(await PdfRequestAsync());
        _env.EmailSender.FailWith = Failure();
        await SendReportAsync(prepared);
        _env.EmailSender.FailWith = null;

        var report = await SendReportAsync(prepared);
        var password = await SendPasswordAsync(prepared);

        Assert.True(report.Succeeded && password.Succeeded);
        Assert.Equal([EmailTemplateDefaults.ReportSubject, EmailTemplateDefaults.PasswordSubject], _env.EmailSender.Sent.Select(m => m.Subject));
        var dispatch = await StoredAsync(prepared.DispatchId);
        Assert.Null(dispatch.ReportError);
        Assert.Equal(DispatchStepStatus.Sent, dispatch.ReportStatus);
    }

    [Fact]
    public async Task When_only_the_password_fails_the_retry_sends_only_the_password_with_the_same_password()
    {
        var prepared = await PrepareOkAsync(await PdfRequestAsync());
        await SendReportAsync(prepared);
        _env.EmailSender.FailWith = Failure(SendErrorCode.Timeout);
        var failed = await SendPasswordAsync(prepared);
        Assert.False(failed.Succeeded);
        Assert.Equal(DispatchStepStatus.Failed, (await StoredAsync(prepared.DispatchId)).PasswordStatus);
        _env.EmailSender.FailWith = null;

        var retry = await SendPasswordAsync(prepared);

        Assert.True(retry.Succeeded);
        Assert.Equal([EmailTemplateDefaults.ReportSubject, EmailTemplateDefaults.PasswordSubject], _env.EmailSender.Sent.Select(m => m.Subject));
        // La password nella seconda email è quella che apre l'allegato già partito.
        Assert.Contains(prepared.Password, _env.EmailSender.Sent[1].Body);
        Assert.Equal(2, TestFiles.PageCount(_env.EmailSender.SentAttachments[0].Single().Content, prepared.Password));
    }

    [Fact]
    public async Task Sending_the_report_again_after_it_was_sent_does_nothing()
    {
        var prepared = await PrepareOkAsync(await PdfRequestAsync());
        await SendReportAsync(prepared);

        var again = await SendReportAsync(prepared);

        Assert.False(again.Attempted);
        Assert.Single(_env.EmailSender.Sent);
    }

    [Fact]
    public async Task Sending_the_password_again_after_it_was_sent_is_refused()
    {
        var prepared = await PrepareOkAsync(await PdfRequestAsync());
        await SendReportAsync(prepared);
        await SendPasswordAsync(prepared);

        await Assert.ThrowsAsync<InvalidOperationException>(() => SendPasswordAsync(prepared));

        Assert.Equal(2, _env.EmailSender.Sent.Count);
    }

    [Fact]
    public async Task After_the_password_is_sent_the_temp_folder_is_gone()
    {
        var request = await PdfRequestAsync();
        var prepared = await PrepareOkAsync(request);
        Assert.True(Directory.Exists(DraftFolder(request.DraftId)));

        await SendReportAsync(prepared);
        Assert.True(Directory.Exists(DraftFolder(request.DraftId)), "serve ancora per un eventuale retry della password");
        await SendPasswordAsync(prepared);

        Assert.False(Directory.Exists(DraftFolder(request.DraftId)));
    }

    [Fact]
    public async Task Abandon_before_sending_marks_both_steps_abandoned_and_deletes_the_folder()
    {
        var request = await PdfRequestAsync();
        var prepared = await PrepareOkAsync(request);

        var result = await WithServiceAsync(s => s.AbandonAsync(prepared));

        Assert.True(result.Succeeded);
        var dispatch = await StoredAsync(prepared.DispatchId);
        Assert.Equal((DispatchStepStatus.Abandoned, DispatchStepStatus.Abandoned), (dispatch.ReportStatus, dispatch.PasswordStatus));
        Assert.False(Directory.Exists(DraftFolder(request.DraftId)));
        Assert.Empty(_env.EmailSender.Sent);
    }

    [Fact]
    public async Task Abandon_after_the_report_was_sent_keeps_it_sent_and_abandons_the_password()
    {
        var prepared = await PrepareOkAsync(await PdfRequestAsync());
        await SendReportAsync(prepared);

        await WithServiceAsync(s => s.AbandonAsync(prepared));

        var dispatch = await StoredAsync(prepared.DispatchId);
        Assert.Equal((DispatchStepStatus.Sent, DispatchStepStatus.Abandoned), (dispatch.ReportStatus, dispatch.PasswordStatus));
        await Assert.ThrowsAsync<InvalidOperationException>(() => SendPasswordAsync(prepared));
    }

    [Fact]
    public async Task A_dispatch_abandoned_by_the_cleanup_cannot_send_anything_anymore()
    {
        var prepared = await PrepareOkAsync(await PdfRequestAsync());
        _env.Clock.Now = _env.Clock.Now.AddMinutes(61);
        using (var scope = _env.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DispatchCleanupService>().RunAsync();
        }

        var report = await SendReportAsync(prepared);

        Assert.False(report.Attempted);
        Assert.False(report.Succeeded);
        Assert.Equal(Messaggi.InvioNonPiuValido, report.UserMessage);
        Assert.Empty(_env.EmailSender.Sent);
    }

    [Fact]
    public async Task Missing_temp_files_fail_the_step_without_sending_anything()
    {
        var request = await PdfRequestAsync();
        var prepared = await PrepareOkAsync(request);
        File.Delete(prepared.ProtectedFilePath);

        var report = await SendReportAsync(prepared);

        Assert.False(report.Succeeded);
        Assert.Equal(Messaggi.FileTemporaneiNonDisponibili, report.UserMessage);
        Assert.Empty(_env.EmailSender.Sent);
        Assert.Equal(DispatchStepStatus.Failed, (await StoredAsync(prepared.DispatchId)).ReportStatus);
    }

    [Fact]
    public async Task Only_the_user_who_created_the_dispatch_can_send_or_abandon_it()
    {
        var prepared = await PrepareOkAsync(await PdfRequestAsync());
        var other = await _env.AddUserAsync("altro", Roles.Operatore);
        _env.CurrentUser.UserId = other.Id;

        var report = await SendReportAsync(prepared);
        var abandon = await WithServiceAsync(s => s.AbandonAsync(prepared));

        Assert.False(report.Attempted);
        Assert.Equal(Messaggi.NonAutorizzato, report.UserMessage);
        Assert.False(abandon.Succeeded);
        Assert.Empty(_env.EmailSender.Sent);
        Assert.Equal(DispatchStepStatus.Pending, (await StoredAsync(prepared.DispatchId)).ReportStatus);
    }

    [Fact]
    public async Task Without_a_logged_in_user_nothing_is_sent()
    {
        var prepared = await PrepareOkAsync(await PdfRequestAsync());
        _env.CurrentUser.UserId = null;

        var report = await SendReportAsync(prepared);

        Assert.False(report.Attempted);
        Assert.Empty(_env.EmailSender.Sent);
    }

    [Fact]
    public async Task The_stored_error_never_contains_the_patient_data()
    {
        var prepared = await PrepareOkAsync(await PdfRequestAsync());
        _env.EmailSender.FailWith = Failure(
            SendErrorCode.RecipientRejected, "550 <mario.rossi@example.test>: Recipient address rejected for Mario Rossi");

        await SendReportAsync(prepared);

        var error = (await StoredAsync(prepared.DispatchId)).ReportError!;
        Assert.DoesNotContain("mario.rossi@example.test", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Rossi", error);
        Assert.DoesNotContain("Mario", error);
        Assert.Contains("RecipientRejected", error);
        Assert.True(error.Length <= 1000);
    }

    [Fact]
    public async Task A_sender_that_throws_leaves_the_step_failed_and_sends_no_password()
    {
        var prepared = await PrepareOkAsync(await PdfRequestAsync());
        using var scope = _env.CreateScope();
        var service = ActivatorUtilities.CreateInstance<DispatchService>(
            scope.ServiceProvider, new ThrowingEmailSender());

        var report = await service.SendReportAsync(prepared);

        Assert.False(report.Succeeded);
        Assert.Equal(DispatchStepStatus.Failed, (await StoredAsync(prepared.DispatchId)).ReportStatus);
        Assert.Empty(_env.EmailSender.Sent);
    }

    private sealed class ThrowingEmailSender : IEmailSender
    {
        public Task<SendResult> SendAsync(EmailMessage message, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task Operatore_manager_and_admin_can_all_send()
    {
        foreach (var (name, role) in new[] { ("op2", Roles.Operatore), ("man", Roles.Manager), ("adm", Roles.Admin), ("sup", Roles.SuperAdmin) })
        {
            var user = await _env.AddUserAsync(name, role);
            _env.CurrentUser.UserId = user.Id;
            var prepared = await PrepareOkAsync(await PdfRequestAsync());
            Assert.True((await SendReportAsync(prepared)).Succeeded);
            Assert.True((await SendPasswordAsync(prepared)).Succeeded);
        }
    }
}
