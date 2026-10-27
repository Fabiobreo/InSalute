using System.Security.Cryptography;
using System.Text.Json;
using InSalute.Core.Data;
using InSalute.Core.Domain;
using InSalute.Core.Email;
using InSalute.Core.Security;
using InSalute.Core.Settings;
using InSalute.Core.Texts;
using InSalute.Core.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InSalute.Core.Dispatching;

/// <summary>
/// Orchestra un invio: prepara il pacchetto protetto, manda prima l'email con il referto e solo se questa risulta
/// <c>Sent</c> (nel database) quella con la password. Nessun altro percorso di codice invia la password.
/// Nei log compare solo il DispatchId.
/// </summary>
public sealed class DispatchService(
    AppDbContext db,
    ActorProvider actors,
    IEmailSender emailSender,
    SmtpSettingsService smtpSettings,
    TempFileStore tempFiles,
    AttachmentProtector protector,
    TimeProvider time,
    ILogger<DispatchService> logger)
{
    private const int MaxErrorLength = 1000;

    private sealed record AttachmentRecord(string FileName, long SizeBytes, string Sha256);

    // ---- PrepareAsync ---------------------------------------------------------------------------------------

    public async Task<OperationResult<PreparedDispatch>> PrepareAsync(DispatchRequest request, CancellationToken ct = default)
    {
        var actor = await actors.GetAllowedAsync(Policies.CanSend, ct);
        if (actor is null)
        {
            return OperationResult<PreparedDispatch>.Fail(Messaggi.NonAutorizzato);
        }
        if (!await smtpSettings.IsConfiguredAsync(ct))
        {
            return OperationResult<PreparedDispatch>.Fail(Messaggi.SmtpNonConfigurato);
        }

        // La UI ha già validato, ma qui non ci si fida di lei.
        var invalid = Validate(request, out var reports, out var invoice);
        if (invalid is not null)
        {
            return OperationResult<PreparedDispatch>.Fail(invalid);
        }
        if (await db.Dispatches.AnyAsync(d => d.Id == request.DraftId, ct))
        {
            return OperationResult<PreparedDispatch>.Fail(Messaggi.InvioGiaPreparato);
        }

        var password = PasswordGenerator.Generate();
        ProtectedPackage? package = null;
        try
        {
            var attachments = new List<AttachmentRecord>();
            foreach (var report in reports)
            {
                await using var stream = File.OpenRead(report.Path);
                var hash = await SHA256.HashDataAsync(stream, ct);
                attachments.Add(new AttachmentRecord(report.OriginalName, stream.Length, Convert.ToHexString(hash).ToLowerInvariant()));
            }

            package = await protector.ProtectAsync(
                reports.Select(r => new ProtectionInput(r.Path, r.OriginalName)).ToList(),
                tempFiles.GetDraftFolder(request.DraftId),
                password,
                ct);

            var now = time.GetUtcNow();
            var firstName = request.FirstName.Trim();
            var lastName = request.LastName.Trim();
            var fileName = DispatchFileName.Build(lastName, firstName, now, package.Extension);

            db.Dispatches.Add(new Dispatch
            {
                Id = request.DraftId,
                CreatedAt = now.UtcDateTime,
                CreatedByUserId = actor.User.Id,
                PatientFirstName = firstName,
                PatientLastName = lastName,
                RecipientEmail = RecipientChecks.NormalizeEmail(request.RecipientEmail),
                AttachmentsJson = JsonSerializer.Serialize(attachments, JsonSerializerOptions.Web),
                InvoiceFileName = invoice?.OriginalName,
                ProtectionKind = package.Kind,
                ReportStatus = DispatchStepStatus.Pending,
                PasswordStatus = DispatchStepStatus.Pending,
            });
            await db.SaveChangesAsync(ct);

            return OperationResult<PreparedDispatch>.Ok(new PreparedDispatch
            {
                DispatchId = request.DraftId,
                Password = password,
                ProtectedFilePath = package.FilePath,
                FileName = fileName,
                ProtectionKind = package.Kind,
                InvoiceFilePath = invoice?.Path,
                InvoiceFileName = invoice?.OriginalName,
            });
        }
        catch (ProtectedPdfException)
        {
            return OperationResult<PreparedDispatch>.Fail(Messaggi.PdfGiaProtetto);
        }
        catch (InvalidPdfException)
        {
            return OperationResult<PreparedDispatch>.Fail(Messaggi.PdfNonLeggibile);
        }
        catch (OperationCanceledException)
        {
            DeleteQuietly(package?.FilePath);
            throw;
        }
        catch (Exception ex)
        {
            DeleteQuietly(package?.FilePath);
            db.ChangeTracker.Clear();
            logger.LogError(ex, "Preparazione dell'invio {DispatchId} non riuscita", request.DraftId);
            return OperationResult<PreparedDispatch>.Fail(Messaggi.ErroreGenerico);
        }
    }

    private sealed record ValidFile(string Path, string OriginalName);

    private string? Validate(DispatchRequest request, out List<ValidFile> reports, out ValidFile? invoice)
    {
        reports = [];
        invoice = null;

        if (request.DraftId == Guid.Empty)
        {
            return Messaggi.ErroreGenerico;
        }
        var firstName = request.FirstName?.Trim() ?? "";
        var lastName = request.LastName?.Trim() ?? "";
        if (lastName.Length == 0)
        {
            return Messaggi.CognomeObbligatorio;
        }
        if (firstName.Length == 0)
        {
            return Messaggi.NomePazienteObbligatorio;
        }
        if (firstName.Length > 100 || lastName.Length > 100)
        {
            return Messaggi.CognomeONomeTroppoLunghi;
        }
        if (!RecipientChecks.IsValidEmail(request.RecipientEmail) || request.RecipientEmail.Trim().Length > 254)
        {
            return Messaggi.EmailNonValida;
        }

        if (request.Reports.Count == 0)
        {
            return Messaggi.NessunReferto;
        }
        if (request.Reports.Count > UploadLimits.MaxReports)
        {
            return Messaggi.TroppiReferti;
        }

        long total = 0;
        foreach (var report in request.Reports)
        {
            var error = CheckFile(request.DraftId, report, out var valid, out var size);
            if (error is not null)
            {
                return error;
            }
            total += size;
            reports.Add(valid!);
        }
        if (total > UploadLimits.MaxTotalBytes)
        {
            return Messaggi.TotaleTroppoGrande;
        }

        if (request.Invoice is not null)
        {
            if (!UploadLimits.IsAllowed(request.Invoice.OriginalFileName) || !AttachmentProtector.IsPdf(request.Invoice.OriginalFileName))
            {
                return Messaggi.FatturaNonPdf;
            }
            var error = CheckFile(request.DraftId, request.Invoice, out invoice, out _);
            if (error is not null)
            {
                return error;
            }
        }
        return null;
    }

    private string? CheckFile(Guid draftId, StoredFile file, out ValidFile? valid, out long size)
    {
        valid = null;
        size = 0;
        if (!UploadLimits.IsAllowed(file.OriginalFileName))
        {
            return Messaggi.TipoFileNonAmmesso(file.OriginalFileName);
        }

        var path = tempFiles.ResolveFile(draftId, file.StoredName);
        if (path is null)
        {
            return Messaggi.FileNonDisponibile;
        }

        size = new FileInfo(path).Length;
        if (size > UploadLimits.MaxFileBytes)
        {
            return Messaggi.FileTroppoGrande(file.OriginalFileName);
        }

        valid = new ValidFile(path, file.OriginalFileName);
        return null;
    }

    // ---- PreviewAsync ---------------------------------------------------------------------------------------

    /// <summary>Segnaposto mostrato al posto della password nell'anteprima: la password si genera solo all'invio.</summary>
    public const string PasswordMask = "••••••••••••";

    /// <summary>
    /// Anteprima delle due email e del nome del file protetto per il riepilogo (6.1, passo 3). Serve il permesso di invio,
    /// non quello di gestire i modelli: l'operatore vede il testo ma non lo modifica.
    /// </summary>
    public async Task<OperationResult<DispatchPreview>> PreviewAsync(
        string firstName, string lastName, IReadOnlyList<string> reportFileNames, CancellationToken ct = default)
    {
        if (await actors.GetAllowedAsync(Policies.CanSend, ct) is null)
        {
            return OperationResult<DispatchPreview>.Fail(Messaggi.NonAutorizzato);
        }
        if (reportFileNames.Count == 0)
        {
            return OperationResult<DispatchPreview>.Fail(Messaggi.NessunReferto);
        }

        var kind = AttachmentProtector.KindFor(reportFileNames);
        var values = new Dictionary<string, string>
        {
            [TemplateRenderer.FirstName] = firstName,
            [TemplateRenderer.LastName] = lastName,
            [TemplateRenderer.Password] = PasswordMask,
        };
        var report = Render(await LoadTemplateAsync(EmailTemplateKind.Report, ct), values);
        var password = Render(await LoadTemplateAsync(EmailTemplateKind.Password, ct), values);
        if (report is null || password is null)
        {
            return OperationResult<DispatchPreview>.Fail(Messaggi.ErroreGenerico);
        }

        var reportBody = kind == ProtectionKind.ZipAes ? report.Value.Body + "\n\n" + Messaggi.NotaArchivioZip : report.Value.Body;
        var extension = kind == ProtectionKind.PdfAes ? ".pdf" : ".zip";
        return OperationResult<DispatchPreview>.Ok(new DispatchPreview(
            kind,
            DispatchFileName.Build(lastName.Trim(), firstName.Trim(), time.GetUtcNow(), extension),
            report.Value.Subject,
            reportBody,
            password.Value.Subject,
            password.Value.Body));
    }

    // ---- SendReportAsync ------------------------------------------------------------------------------------

    /// <summary>Invia la prima email. Se il referto risulta già inviato (o l'invio è abbandonato) non fa nulla.</summary>
    public async Task<DispatchStepResult> SendReportAsync(PreparedDispatch prepared, CancellationToken ct = default)
    {
        var (dispatch, denied) = await LoadOwnDispatchAsync(prepared, ct);
        if (dispatch is null)
        {
            return DispatchStepResult.NotAttempted(denied);
        }

        if (dispatch.ReportStatus == DispatchStepStatus.Sent)
        {
            return new DispatchStepResult(false, true);
        }
        if (dispatch.ReportStatus is not (DispatchStepStatus.Pending or DispatchStepStatus.Failed))
        {
            // Abbandonato (dall'operatore o dalla pulizia periodica): riprovare non servirebbe.
            return DispatchStepResult.NotAttempted(Messaggi.InvioNonPiuValido);
        }

        var template = await LoadTemplateAsync(EmailTemplateKind.Report, ct);
        var rendered = Render(template, dispatch, password: null);
        if (rendered is null)
        {
            return await FailStepAsync(dispatch, report: true, Messaggi.ErroreGenerico, "Modello email non valido.");
        }

        var (subject, body) = rendered.Value;
        if (dispatch.ProtectionKind == ProtectionKind.ZipAes)
        {
            body += "\n\n" + Messaggi.NotaArchivioZip;
        }

        var attachments = new List<EmailAttachment>();
        if (!IsInsideDraftFolder(prepared, prepared.ProtectedFilePath) || !File.Exists(prepared.ProtectedFilePath))
        {
            return await FailStepAsync(dispatch, report: true, Messaggi.FileTemporaneiNonDisponibili, "File protetto mancante.");
        }
        attachments.Add(new EmailAttachment(prepared.ProtectedFilePath, prepared.FileName));
        if (prepared.InvoiceFilePath is not null)
        {
            if (!IsInsideDraftFolder(prepared, prepared.InvoiceFilePath) || !File.Exists(prepared.InvoiceFilePath))
            {
                return await FailStepAsync(dispatch, report: true, Messaggi.FileTemporaneiNonDisponibili, "Fattura mancante.");
            }
            attachments.Add(new EmailAttachment(prepared.InvoiceFilePath, prepared.InvoiceFileName ?? "Fattura.pdf"));
        }

        var result = await SendSafelyAsync(new EmailMessage(dispatch.RecipientEmail, subject, body, attachments), ct);
        return await RecordResultAsync(dispatch, report: true, result);
    }

    // ---- SendPasswordAsync ----------------------------------------------------------------------------------

    /// <summary>
    /// Invia la seconda email, con la stessa password dell'allegato già partito. Precondizione (letta dal database):
    /// <c>ReportStatus == Sent</c> e <c>PasswordStatus</c> in {Pending, Failed}, altrimenti <see cref="InvalidOperationException"/>.
    /// </summary>
    public async Task<DispatchStepResult> SendPasswordAsync(PreparedDispatch prepared, CancellationToken ct = default)
    {
        var (dispatch, denied) = await LoadOwnDispatchAsync(prepared, ct);
        if (dispatch is null)
        {
            return DispatchStepResult.NotAttempted(denied);
        }

        if (dispatch.ReportStatus != DispatchStepStatus.Sent
            || dispatch.PasswordStatus is not (DispatchStepStatus.Pending or DispatchStepStatus.Failed))
        {
            throw new InvalidOperationException(
                "La password si può inviare solo dopo il referto e solo se non è già stata inviata o abbandonata.");
        }

        var template = await LoadTemplateAsync(EmailTemplateKind.Password, ct);
        var rendered = Render(template, dispatch, prepared.Password);
        if (rendered is null)
        {
            return await FailStepAsync(dispatch, report: false, Messaggi.ErroreGenerico, "Modello email non valido.");
        }

        var (subject, body) = rendered.Value;
        var result = await SendSafelyAsync(new EmailMessage(dispatch.RecipientEmail, subject, body), ct);
        var step = await RecordResultAsync(dispatch, report: false, result);
        if (step.Succeeded)
        {
            tempFiles.DeleteDraft(dispatch.Id);
        }
        return step;
    }

    // ---- AbandonAsync ---------------------------------------------------------------------------------------

    /// <summary>Gli step non inviati diventano <c>Abandoned</c> e la cartella temporanea viene cancellata.</summary>
    public async Task<OperationResult> AbandonAsync(PreparedDispatch prepared, CancellationToken ct = default)
    {
        var (dispatch, denied) = await LoadOwnDispatchAsync(prepared, ct);
        if (dispatch is null)
        {
            return OperationResult.Fail(denied ?? Messaggi.ErroreGenerico);
        }

        if (dispatch.ReportStatus != DispatchStepStatus.Sent)
        {
            dispatch.ReportStatus = DispatchStepStatus.Abandoned;
        }
        if (dispatch.PasswordStatus != DispatchStepStatus.Sent)
        {
            dispatch.PasswordStatus = DispatchStepStatus.Abandoned;
        }
        await db.SaveChangesAsync(CancellationToken.None);

        tempFiles.DeleteDraft(dispatch.Id);
        return OperationResult.Ok();
    }

    // ---- interni --------------------------------------------------------------------------------------------

    /// <summary>
    /// Permessi e stato dal database, mai dall'oggetto in memoria: l'utente deve poter inviare ed essere chi ha creato l'invio.
    /// </summary>
    private async Task<(Dispatch? Dispatch, string? Error)> LoadOwnDispatchAsync(PreparedDispatch prepared, CancellationToken ct)
    {
        var actor = await actors.GetAllowedAsync(Policies.CanSend, ct);
        if (actor is null)
        {
            return (null, Messaggi.NonAutorizzato);
        }

        var dispatch = await db.Dispatches.FirstOrDefaultAsync(d => d.Id == prepared.DispatchId, ct);
        if (dispatch is null)
        {
            return (null, Messaggi.InvioNonTrovato);
        }
        if (dispatch.CreatedByUserId != actor.User.Id)
        {
            return (null, Messaggi.NonAutorizzato);
        }
        return (dispatch, null);
    }

    private async Task<EmailTemplate?> LoadTemplateAsync(EmailTemplateKind kind, CancellationToken ct) =>
        await db.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Kind == kind, ct);

    private static (string Subject, string Body)? Render(EmailTemplate? template, Dispatch dispatch, string? password)
    {
        var values = new Dictionary<string, string>
        {
            [TemplateRenderer.FirstName] = dispatch.PatientFirstName,
            [TemplateRenderer.LastName] = dispatch.PatientLastName,
        };
        if (password is not null)
        {
            values[TemplateRenderer.Password] = password;
        }
        return Render(template, values);
    }

    private static (string Subject, string Body)? Render(EmailTemplate? template, IReadOnlyDictionary<string, string> values)
    {
        if (template is null)
        {
            return null;
        }

        var subject = TemplateRenderer.Render(template.Subject, values);
        var body = TemplateRenderer.Render(template.Body, values);
        return subject.Succeeded && body.Succeeded ? (subject.Text, body.Text) : null;
    }

    private async Task<SendResult> SendSafelyAsync(EmailMessage message, CancellationToken ct)
    {
        try
        {
            return await emailSender.SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // IEmailSender non dovrebbe lanciare, ma un'eccezione non deve lasciare lo step in uno stato incerto.
            return SendResult.Fail(SendErrorCode.Unknown, ex.GetType().Name);
        }
    }

    private async Task<DispatchStepResult> RecordResultAsync(Dispatch dispatch, bool report, SendResult result)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var status = result.Success ? DispatchStepStatus.Sent : DispatchStepStatus.Failed;
        var error = result.Success ? null : TechnicalError(dispatch, result);

        if (report)
        {
            dispatch.ReportStatus = status;
            dispatch.ReportSentAt = result.Success ? now : null;
            dispatch.ReportError = error;
        }
        else
        {
            dispatch.PasswordStatus = status;
            dispatch.PasswordSentAt = result.Success ? now : null;
            dispatch.PasswordError = error;
        }

        // L'email può essere già partita: l'esito si registra anche se l'utente ha chiuso la pagina nel frattempo.
        await db.SaveChangesAsync(CancellationToken.None);

        return result.Success
            ? new DispatchStepResult(true, true)
            : new DispatchStepResult(true, false, Messaggi.PerErroreInvio(result.ErrorCode), result.TechnicalDetails);
    }

    private async Task<DispatchStepResult> FailStepAsync(Dispatch dispatch, bool report, string userMessage, string technical)
    {
        if (report)
        {
            dispatch.ReportStatus = DispatchStepStatus.Failed;
            dispatch.ReportError = technical;
        }
        else
        {
            dispatch.PasswordStatus = DispatchStepStatus.Failed;
            dispatch.PasswordError = technical;
        }
        await db.SaveChangesAsync(CancellationToken.None);
        return new DispatchStepResult(true, false, userMessage, technical);
    }

    /// <summary>Codice e dettagli dell'errore, senza indirizzo, nome e cognome che l'SMTP a volte ripete nei messaggi.</summary>
    private static string TechnicalError(Dispatch dispatch, SendResult result)
    {
        var details = result.TechnicalDetails ?? "";
        foreach (var secret in new[] { dispatch.RecipientEmail, dispatch.PatientLastName, dispatch.PatientFirstName })
        {
            if (secret.Length >= 3)
            {
                details = details.Replace(secret, "[omesso]", StringComparison.OrdinalIgnoreCase);
            }
        }

        var text = $"{result.ErrorCode}: {details}".TrimEnd(' ', ':');
        return text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];
    }

    private bool IsInsideDraftFolder(PreparedDispatch prepared, string path)
    {
        var folder = Path.GetFullPath(tempFiles.GetDraftFolder(prepared.DispatchId));
        return string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), folder, StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteQuietly(string? path)
    {
        if (path is null)
        {
            return;
        }
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Resta alla pulizia periodica.
        }
    }
}
