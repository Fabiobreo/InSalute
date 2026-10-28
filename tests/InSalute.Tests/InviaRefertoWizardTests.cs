using Bunit;
using Bunit.TestDoubles;
using ICSharpCode.SharpZipLib.Zip;
using InSalute.Core.Data;
using InSalute.Core.Dispatching;
using InSalute.Core.Domain;
using InSalute.Core.Email;
using InSalute.Core.Security;
using InSalute.Core.Settings;
using InSalute.Core.Texts;
using InSalute.Web.Components.Pages.InviaReferto;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using PdfSharp.Pdf.IO;
using Xunit;
using AngleSharp.Dom;

namespace InSalute.Tests;

/// <summary>
/// Procedura guidata "Invia referto" (Fase 5). Il DbContext è uno solo per tutto il test, come nel circuito Blazor.
/// I dati sono finti: nessun paziente reale.
/// </summary>
public sealed class InviaRefertoWizardTests : IAsyncLifetime
{
    private readonly TestEnvironment _env = new();
    private readonly BunitContext _ctx = new();
    private IServiceScope _scope = default!;
    private ApplicationUser _operator = default!;
    private IRenderedComponent<InviaReferto> _cut = default!;

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

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
        _scope?.Dispose();
        await _env.DisposeAsync();
    }

    private void Render(bool smtpConfigured = true)
    {
        if (!smtpConfigured)
        {
            using var scope = _env.CreateScope();
            scope.ServiceProvider.GetRequiredService<AppDbContext>().SmtpSettings.ExecuteDelete();
        }

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddLogging();
        _ctx.AddAuthorization().SetAuthorized("operatore").SetPolicies(Policies.CanSend);

        // Un unico scope condiviso, come il circuito Blazor.
        _scope = _env.CreateScope();
        foreach (var type in new[]
                 {
                     typeof(DispatchService), typeof(RecipientChecks), typeof(SmtpSettingsService),
                     typeof(TempFileStore), typeof(AttachmentProtector), typeof(TimeProvider),
                 })
        {
            _ctx.Services.AddSingleton(type, _scope.ServiceProvider.GetRequiredService(type));
        }

        _cut = _ctx.Render<InviaReferto>();
    }

    // ---- helper UI --------------------------------------------------------------------------------------------

    private IElement Button(string text) =>
        _cut.FindAll("button").Single(b => b.TextContent.Trim() == text);

    /// <summary>Il click passa dal dispatcher del componente: tra "trova" e "clicca" non può avvenire un nuovo rendering.</summary>
    private void Click(string text) => _cut.InvokeAsync(() => Button(text).Click()).GetAwaiter().GetResult();

    private bool HasButton(string text) => _cut.FindAll("button").Any(b => b.TextContent.Trim() == text);

    private static bool IsDisabled(IElement button) => button.HasAttribute("disabled");

    private void FillPatient(string last = "Rossi", string first = "Mario", string email = "mario.rossi@example.test", string? confirm = null)
    {
        var inputs = _cut.FindAll("input");
        inputs[0].Input(last);
        inputs[1].Input(first);
        inputs[2].Input(email);
        _cut.FindAll("input")[3].Input(confirm ?? email);
    }

    private async Task UploadReportsAsync(params (string Name, byte[] Content)[] files)
    {
        var contents = files.Select(f => InputFileContent.CreateFromBinary(f.Content, f.Name)).ToArray();
        _cut.FindComponents<InputFile>()[0].UploadFiles(contents);
        WaitForUploads(files.Select(f => f.Name));
        await Task.CompletedTask;
    }

    /// <summary>Il caricamento è asincrono: si aspetta che ogni file compaia come scheda (accettato o rifiutato).</summary>
    private void WaitForUploads(IEnumerable<string> names) =>
        _cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Caricamento in corso", _cut.Markup);
            foreach (var name in names)
            {
                Assert.Contains(name, _cut.Markup);
            }
        });

    private async Task GoToSummaryAsync(params (string Name, byte[] Content)[] files)
    {
        FillPatient();
        Button("Avanti").Click();
        await UploadReportsAsync(files);
        _cut.WaitForState(() => !IsDisabled(Button("Avanti")));
        Button("Avanti").Click();
        _cut.WaitForAssertion(() => Assert.Contains("Ho verificato", _cut.Markup));
    }

    private async Task SendAsync()
    {
        _cut.Find("input[type=checkbox]").Change(true);
        Button("Invia referto").Click();
        await Task.CompletedTask;
    }

    private static string? PasswordFrom(EmailMessage password)
    {
        // Il modello di default termina la frase con {PASSWORD}: è l'ultima parola della riga.
        var line = password.Body.Split('\n').Single(l => l.Contains("password con cui"));
        return line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];
    }

    private string[] LeftoverDraftFolders() =>
        Directory.Exists(_env.TempPath) ? Directory.GetDirectories(_env.TempPath) : [];

    private async Task<Dispatch> StoredDispatchAsync()
    {
        using var scope = _env.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Dispatches.AsNoTracking().SingleAsync();
    }

    private static readonly (string, byte[])[] OnePdf = [("referto.pdf", TestFiles.Pdf(2))];

    // ---- obbligatori del piano (sezione 11) --------------------------------------------------------------------

    [Fact]
    public void Next_is_disabled_when_the_two_emails_do_not_match()
    {
        Render();

        FillPatient(confirm: "altro.indirizzo@example.test");
        Assert.True(IsDisabled(Button("Avanti")));

        _cut.FindAll("input")[3].Input("MARIO.ROSSI@example.test ");
        Assert.False(IsDisabled(Button("Avanti")));
    }

    [Fact]
    public async Task Send_is_disabled_until_the_checkbox_is_ticked()
    {
        Render();
        await GoToSummaryAsync(OnePdf);

        Assert.True(IsDisabled(Button("Invia referto")));
        _cut.Find("input[type=checkbox]").Change(true);
        Assert.False(IsDisabled(Button("Invia referto")));
    }

    [Fact]
    public async Task When_the_first_email_fails_only_retry_report_is_offered_and_the_password_is_not_sent()
    {
        Render();
        await GoToSummaryAsync(OnePdf);
        _env.EmailSender.FailWith = SendResult.Fail(SendErrorCode.ConnectionFailed, "boom");

        await SendAsync();

        _cut.WaitForAssertion(() => Assert.True(HasButton("Riprova invio referto")));
        Assert.False(HasButton("Riprova invio password"));
        Assert.True(HasButton("Annulla invio"));
        Assert.Contains(Messaggi.PerErroreInvio(SendErrorCode.ConnectionFailed), _cut.Markup);
        Assert.Contains("Dettagli tecnici", _cut.Markup);
        Assert.Empty(_env.EmailSender.Sent);
    }

    // ---- flussi completi ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Single_pdf_is_sent_protected_then_the_password_and_the_temp_folder_is_emptied()
    {
        Render();
        await GoToSummaryAsync(OnePdf);
        await SendAsync();

        _cut.WaitForAssertion(() => Assert.Contains("Referto e password inviati a mario.rossi@example.test.", _cut.Markup));
        Assert.True(HasButton("Nuovo invio"));

        Assert.Equal(2, _env.EmailSender.Sent.Count);
        Assert.Empty(_env.EmailSender.Sent[1].Attachments ?? []);
        var password = PasswordFrom(_env.EmailSender.Sent[1])!;
        Assert.Equal(12, password.Length);

        var attachment = Assert.Single(_env.EmailSender.SentAttachments[0]);
        Assert.StartsWith("Referto_Rossi_Mario_", attachment.FileName);
        Assert.ThrowsAny<PdfReaderException>(() => TestFiles.PageCount(attachment.Content));
        Assert.Equal(2, TestFiles.PageCount(attachment.Content, password));

        Assert.Empty(LeftoverDraftFolders());
        var stored = await StoredDispatchAsync();
        Assert.Equal(DispatchStepStatus.Sent, stored.ReportStatus);
        Assert.Equal(DispatchStepStatus.Sent, stored.PasswordStatus);
    }

    [Fact]
    public async Task Three_pdfs_become_a_single_pdf_with_all_the_pages()
    {
        Render();
        await GoToSummaryAsync(("a.pdf", TestFiles.Pdf(1)), ("b.pdf", TestFiles.Pdf(2)), ("c.PDF", TestFiles.Pdf(3)));
        Assert.Contains("unico PDF protetto da password", _cut.Markup);
        await SendAsync();

        _cut.WaitForAssertion(() => Assert.True(HasButton("Nuovo invio")));
        var attachment = Assert.Single(_env.EmailSender.SentAttachments[0]);
        Assert.EndsWith(".pdf", attachment.FileName);
        Assert.Equal(6, TestFiles.PageCount(attachment.Content, PasswordFrom(_env.EmailSender.Sent[1])));
    }

    [Fact]
    public async Task Pdf_plus_image_becomes_an_aes_256_zip_and_the_invoice_travels_unprotected()
    {
        Render();
        FillPatient();
        Button("Avanti").Click();
        await UploadReportsAsync(("referto.pdf", TestFiles.Pdf(1)), ("foto.jpg", TestFiles.Image()));
        _cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromBinary(TestFiles.Pdf(7), "fattura.pdf"));
        WaitForUploads(["fattura.pdf"]);
        _cut.WaitForState(() => !IsDisabled(Button("Avanti")));
        Button("Avanti").Click();
        _cut.WaitForAssertion(() => Assert.Contains("archivio ZIP protetto da password", _cut.Markup));
        Assert.Contains("allegata senza password", _cut.Markup);
        await SendAsync();

        _cut.WaitForAssertion(() => Assert.True(HasButton("Nuovo invio")));
        var password = PasswordFrom(_env.EmailSender.Sent[1])!;
        var attachments = _env.EmailSender.SentAttachments[0];
        Assert.Equal(2, attachments.Count);
        Assert.Contains(Messaggi.NotaArchivioZip, _env.EmailSender.Sent[0].Body);

        var zipFile = attachments.Single(a => a.FileName.EndsWith(".zip"));
        using (var zip = new ZipFile(new MemoryStream(zipFile.Content)))
        {
            foreach (ZipEntry entry in zip)
            {
                Assert.Equal(256, entry.AESKeySize);
            }
            Assert.Equal(2, zip.Count);
        }

        var invoice = attachments.Single(a => a.FileName == "fattura.pdf");
        Assert.Equal(7, TestFiles.PageCount(invoice.Content));
        Assert.Equal(12, password.Length);
    }

    [Fact]
    public async Task Retry_after_a_failed_first_email_sends_the_report_and_then_the_password()
    {
        Render();
        await GoToSummaryAsync(OnePdf);
        _env.EmailSender.FailWith = SendResult.Fail(SendErrorCode.AuthenticationFailed, "no");
        await SendAsync();
        _cut.WaitForAssertion(() => Assert.True(HasButton("Riprova invio referto")));

        _env.EmailSender.FailWith = null;
        Click("Riprova invio referto");

        _cut.WaitForAssertion(() => Assert.True(HasButton("Nuovo invio")));
        Assert.Equal(2, _env.EmailSender.Sent.Count);
        Assert.NotEmpty(_env.EmailSender.Sent[0].Attachments!);
        Assert.Empty(_env.EmailSender.Sent[1].Attachments ?? []);
    }

    [Fact]
    public async Task When_only_the_password_fails_the_retry_sends_only_the_password_with_the_same_value()
    {
        Render();
        await GoToSummaryAsync(OnePdf);
        // La prima email passa, la seconda no.
        _env.EmailSender.FailAfter = 1;

        await SendAsync();
        _cut.WaitForAssertion(() => Assert.True(HasButton("Riprova invio password")));
        Assert.False(HasButton("Riprova invio referto"));
        Assert.Single(_env.EmailSender.Sent);

        _env.EmailSender.FailAfter = null;
        Click("Riprova invio password");

        _cut.WaitForAssertion(() => Assert.True(HasButton("Nuovo invio")));
        Assert.Equal(2, _env.EmailSender.Sent.Count);
        var attachment = Assert.Single(_env.EmailSender.SentAttachments[0]);
        Assert.Equal(2, TestFiles.PageCount(attachment.Content, PasswordFrom(_env.EmailSender.Sent[1])));
    }

    [Fact]
    public async Task Cancelling_after_the_report_was_sent_asks_for_confirmation_and_keeps_the_report_as_sent()
    {
        Render();
        var dialogs = _ctx.Render<MudDialogProvider>();
        await GoToSummaryAsync(OnePdf);
        _env.EmailSender.FailAfter = 1;
        await SendAsync();
        _cut.WaitForAssertion(() => Assert.True(HasButton("Riprova invio password")));

        Click("Annulla");

        dialogs.WaitForAssertion(() => Assert.Contains("Se annulli dovrai rifare", dialogs.Markup));
        Assert.Equal(DispatchStepStatus.Failed, (await StoredDispatchAsync()).PasswordStatus);

        dialogs.Find("button.mud-button-filled").Click();

        _cut.WaitForAssertion(() => Assert.Contains("Cognome", _cut.Markup));
        var stored = await StoredDispatchAsync();
        Assert.Equal(DispatchStepStatus.Sent, stored.ReportStatus);
        Assert.Equal(DispatchStepStatus.Abandoned, stored.PasswordStatus);
        Assert.Empty(LeftoverDraftFolders());
    }

    [Fact]
    public void Enter_in_a_field_goes_on_only_when_the_step_is_valid()
    {
        Render();
        FillPatient(confirm: "diversa@example.test");

        _cut.Find("form").Submit();
        Assert.DoesNotContain("Trascina qui i referti", _cut.Markup);

        _cut.FindAll("input")[3].Input("mario.rossi@example.test");
        _cut.Find("form").Submit();
        Assert.Contains("Trascina qui i referti", _cut.Markup);
    }

    [Fact]
    public async Task Cancel_after_a_failed_report_abandons_the_dispatch_clears_the_folder_and_restarts()
    {
        Render();
        await GoToSummaryAsync(OnePdf);
        _env.EmailSender.FailWith = SendResult.Fail(SendErrorCode.Timeout, "t");
        await SendAsync();
        _cut.WaitForAssertion(() => Assert.True(HasButton("Annulla invio")));

        Click("Annulla invio");

        _cut.WaitForAssertion(() => Assert.Contains("Cognome", _cut.Markup));
        var stored = await StoredDispatchAsync();
        Assert.Equal(DispatchStepStatus.Abandoned, stored.ReportStatus);
        Assert.Equal(DispatchStepStatus.Abandoned, stored.PasswordStatus);
        Assert.Empty(LeftoverDraftFolders());
        Assert.Empty(_env.EmailSender.Sent);
    }

    [Fact]
    public async Task New_dispatch_restarts_with_empty_fields()
    {
        Render();
        await GoToSummaryAsync(OnePdf);
        await SendAsync();
        _cut.WaitForAssertion(() => Assert.True(HasButton("Nuovo invio")));

        Click("Nuovo invio");

        _cut.WaitForAssertion(() => Assert.Equal("", _cut.FindAll("input")[0].GetAttribute("value") ?? ""));
        Assert.Equal("", _cut.FindAll("input")[2].GetAttribute("value") ?? "");
    }

    // ---- passo 1: avvisi ---------------------------------------------------------------------------------------

    [Fact]
    public void A_domain_typo_suggests_the_right_one_and_correct_fixes_both_fields()
    {
        Render();
        FillPatient(email: "mario@gmial.com");

        Assert.Contains("Forse intendevi mario@gmail.com?", _cut.Markup);
        Button("Correggi").Click();

        Assert.DoesNotContain("Forse intendevi", _cut.Markup);
        Assert.Equal("mario@gmail.com", _cut.FindAll("input")[2].GetAttribute("value"));
        Assert.Equal("mario@gmail.com", _cut.FindAll("input")[3].GetAttribute("value"));
        Assert.False(IsDisabled(Button("Avanti")));
    }

    [Fact]
    public async Task An_address_used_before_for_another_surname_shows_a_warning_without_naming_the_patient()
    {
        using (var scope = _env.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Dispatches.Add(new Dispatch
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = _operator.Id,
                PatientFirstName = "Anna",
                PatientLastName = "Bianchi",
                RecipientEmail = "famiglia@example.test",
                AttachmentsJson = "[]",
                ReportStatus = DispatchStepStatus.Sent,
                PasswordStatus = DispatchStepStatus.Sent,
            });
            await db.SaveChangesAsync();
        }
        Render();

        FillPatient(email: "famiglia@example.test");

        _cut.WaitForAssertion(() => Assert.Contains("usato in passato per un altro paziente", _cut.Markup));
        Assert.DoesNotContain("Bianchi", _cut.Markup);
    }

    [Fact]
    public void Field_errors_appear_under_the_field_after_leaving_it()
    {
        Render();

        _cut.FindAll("input")[2].Input("non-una-email");
        _cut.FindAll("input")[2].Blur();

        Assert.Contains(Messaggi.EmailNonValida, _cut.FindAll(".mud-input-helper-text").Select(e => e.TextContent));
    }

    // ---- passo 2: documenti ------------------------------------------------------------------------------------

    [Fact]
    public async Task An_already_protected_pdf_is_rejected_with_the_message_and_not_kept_on_disk()
    {
        Render();
        FillPatient();
        Button("Avanti").Click();

        await UploadReportsAsync(("protetto.pdf", TestFiles.ProtectedPdf("segreta123")));

        Assert.Contains(Messaggi.PdfGiaProtetto, _cut.Markup);
        Assert.True(IsDisabled(Button("Avanti")));
        Assert.Empty(Directory.EnumerateFiles(_env.TempPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_truncated_pdf_is_rejected_as_unreadable_without_closing_the_procedure()
    {
        Render();
        FillPatient();
        Button("Avanti").Click();
        var pdf = TestFiles.Pdf(2);

        // PDFsharp lancia una System.Exception generica su un file troncato.
        await UploadReportsAsync(("troncato.pdf", pdf[..(pdf.Length / 2)]));
        Assert.Contains(Messaggi.PdfNonLeggibile, _cut.Markup);
        Assert.Empty(Directory.EnumerateFiles(_env.TempPath, "*", SearchOption.AllDirectories));

        await UploadReportsAsync(("buono.pdf", TestFiles.Pdf(1)));
        Assert.False(IsDisabled(Button("Avanti")));
    }

    /// <summary>Sceglie dei file come farebbe il browser, ma con stream controllati dal test.</summary>
    private Task ChooseAsync(int input, params IBrowserFile[] files) =>
        _cut.InvokeAsync(() => _cut.FindComponents<InputFile>()[input].Instance.OnChange
            .InvokeAsync(new InputFileChangeEventArgs(files)));

    [Fact]
    public async Task A_report_whose_connection_drops_is_rejected_and_the_procedure_can_go_on()
    {
        _env.Storage.UploadIdleTimeout = TimeSpan.FromMilliseconds(300);
        Render();
        FillPatient();
        Button("Avanti").Click();

        var choosing = ChooseAsync(0,
            new StreamBrowserFile("bloccato.pdf", 10, () => new StalledStream([1, 2, 3])),
            new StreamBrowserFile("buono.pdf", TestFiles.Pdf(1).Length, () => new MemoryStream(TestFiles.Pdf(1))));

        // Prima che scada l'attesa il caricamento è in corso e non si può andare avanti.
        Assert.Contains("Caricamento in corso", _cut.Markup);
        Assert.True(IsDisabled(Button("Avanti")));

        await choosing.WaitAsync(TimeSpan.FromSeconds(10));
        WaitForUploads(["bloccato.pdf", "buono.pdf"]);
        Assert.Contains(Messaggi.CaricamentoInterrotto, _cut.Markup);
        Assert.False(IsDisabled(Button("Avanti")));
        Assert.Single(Directory.EnumerateFiles(_env.TempPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Next_stays_disabled_while_reports_are_uploading_even_if_the_invoice_has_finished()
    {
        _env.Storage.UploadIdleTimeout = TimeSpan.FromMilliseconds(500);
        Render();
        FillPatient();
        Button("Avanti").Click();
        await UploadReportsAsync(("primo.pdf", TestFiles.Pdf(1)));

        var reports = ChooseAsync(0, new StreamBrowserFile("lento.pdf", 10, () => new StalledStream([])));
        await ChooseAsync(1, new StreamBrowserFile("fattura.pdf", TestFiles.Pdf(1).Length, () => new MemoryStream(TestFiles.Pdf(1))));

        // La fattura è arrivata, ma un referto è ancora in caricamento.
        _cut.WaitForAssertion(() => Assert.Contains("fattura.pdf", _cut.Markup));
        Assert.Contains("Caricamento in corso", _cut.Markup);
        Assert.True(IsDisabled(Button("Avanti")));

        await reports.WaitAsync(TimeSpan.FromSeconds(10));
        _cut.WaitForState(() => !IsDisabled(Button("Avanti")));
    }

    [Fact]
    public async Task Choosing_more_than_a_hundred_files_rejects_the_extra_ones_instead_of_failing()
    {
        Render();
        FillPatient();
        Button("Avanti").Click();
        var files = Enumerable.Range(1, 101).Select(i => ($"nota{i:000}.txt", new byte[] { 1, 2, 3 })).ToArray();

        await UploadReportsAsync(files);

        Assert.Equal(UploadLimits.MaxReports, Directory.EnumerateFiles(_env.TempPath, "*", SearchOption.AllDirectories).Count());
        Assert.Contains(Messaggi.TroppiReferti, _cut.Markup);
        Assert.Contains("nota101.txt", _cut.Markup);
    }

    [Fact]
    public async Task File_type_is_checked_case_insensitively_and_unknown_types_are_rejected()
    {
        Render();
        FillPatient();
        Button("Avanti").Click();

        await UploadReportsAsync(("MAIUSCOLO.PDF", TestFiles.Pdf(3)), ("programma.exe", [1, 2, 3]));

        Assert.Contains("MAIUSCOLO.PDF", _cut.Markup);
        Assert.Contains("3 pagine", _cut.Markup);
        Assert.Contains(Messaggi.TipoFileNonAmmesso("programma.exe"), _cut.Markup);
        Assert.False(IsDisabled(Button("Avanti")));
    }

    [Fact]
    public async Task Uploading_more_files_adds_them_and_the_remove_button_deletes_a_single_file()
    {
        Render();
        FillPatient();
        Button("Avanti").Click();

        await UploadReportsAsync(("uno.pdf", TestFiles.Pdf(1)));
        await UploadReportsAsync(("due.pdf", TestFiles.Pdf(1)));
        Assert.Contains("uno.pdf", _cut.Markup);
        Assert.Contains("due.pdf", _cut.Markup);
        Assert.Equal(2, Directory.EnumerateFiles(_env.TempPath, "*", SearchOption.AllDirectories).Count());

        _cut.Find("button[aria-label='Rimuovi uno.pdf']").Click();

        Assert.DoesNotContain("uno.pdf", _cut.Markup);
        Assert.Single(Directory.EnumerateFiles(_env.TempPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_warning_appears_when_no_file_name_contains_the_surname()
    {
        Render();
        FillPatient(last: "Verdi");
        Button("Avanti").Click();

        await UploadReportsAsync(("scansione.pdf", TestFiles.Pdf(1)));
        Assert.Contains("Nessun file contiene il cognome «Verdi»", _cut.Markup);

        await UploadReportsAsync(("Verdi analisi.pdf", TestFiles.Pdf(1)));
        Assert.DoesNotContain("Nessun file contiene il cognome", _cut.Markup);
    }

    [Fact]
    public void The_invoice_area_says_it_is_attached_without_a_password()
    {
        Render();
        FillPatient();
        Button("Avanti").Click();

        Assert.Contains("La fattura sarà allegata senza password.", _cut.Markup);
    }

    // ---- file scaduti e invii abbandonati dalla pulizia --------------------------------------------------------

    [Fact]
    public async Task Files_deleted_by_the_cleanup_send_the_user_back_to_documents_with_the_files_marked()
    {
        Render();
        await GoToSummaryAsync(OnePdf);
        // La pulizia periodica ha cancellato la cartella dell'invio mentre l'operatore era al riepilogo.
        Directory.Delete(Assert.Single(LeftoverDraftFolders()), recursive: true);

        await SendAsync();

        _cut.WaitForAssertion(() => Assert.Contains(Messaggi.FileScaduto, _cut.Markup));
        Assert.Contains("Trascina qui i referti", _cut.Markup);
        Assert.True(IsDisabled(Button("Avanti")));
        Assert.Empty(_env.EmailSender.Sent);
        using var scope = _env.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<AppDbContext>().Dispatches);
    }

    [Fact]
    public async Task A_report_retry_on_a_dispatch_abandoned_by_the_cleanup_is_not_offered_again()
    {
        Render();
        await GoToSummaryAsync(OnePdf);
        _env.EmailSender.FailWith = SendResult.Fail(SendErrorCode.ConnectionFailed, "boom");
        await SendAsync();
        _cut.WaitForAssertion(() => Assert.True(HasButton("Riprova invio referto")));

        _env.Clock.Now = _env.Clock.Now.AddMinutes(61);
        using (var scope = _env.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DispatchCleanupService>().RunAsync();
        }
        _env.EmailSender.FailWith = null;
        Click("Riprova invio referto");

        _cut.WaitForAssertion(() => Assert.Contains(Messaggi.InvioNonPiuValido, _cut.Markup));
        Assert.False(HasButton("Riprova invio referto"));
        Assert.True(HasButton("Annulla invio"));
        Assert.Empty(_env.EmailSender.Sent);
    }

    // ---- passo 3 / avviso SMTP ---------------------------------------------------------------------------------

    [Fact]
    public async Task Summary_shows_recipient_and_both_emails_with_the_password_masked()
    {
        Render();
        await GoToSummaryAsync(OnePdf);

        Assert.Contains("Mario Rossi, mario.rossi@example.test", _cut.Markup);
        Assert.Contains("Buongiorno Mario Rossi", _cut.Markup);
        Assert.Contains(DispatchService.PasswordMask, _cut.Markup);
        Assert.Contains("referto.pdf", _cut.Markup);
    }

    [Fact]
    public async Task Without_smtp_settings_a_warning_is_shown_and_sending_stays_disabled()
    {
        Render(smtpConfigured: false);
        Assert.Contains("non sono ancora configurate", _cut.Markup);

        await GoToSummaryAsync(OnePdf);
        _cut.Find("input[type=checkbox]").Change(true);

        Assert.True(IsDisabled(Button("Invia referto")));
    }

    [Fact]
    public async Task Leaving_the_page_before_sending_deletes_the_draft_folder()
    {
        Render();
        FillPatient();
        Button("Avanti").Click();
        await UploadReportsAsync(OnePdf);
        Assert.NotEmpty(LeftoverDraftFolders());

        await _ctx.DisposeComponentsAsync();

        Assert.Empty(LeftoverDraftFolders());
    }
}
