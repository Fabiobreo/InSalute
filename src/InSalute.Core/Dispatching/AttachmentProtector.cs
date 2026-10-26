using ICSharpCode.SharpZipLib.Zip;
using InSalute.Core.Domain;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Security;

namespace InSalute.Core.Dispatching;

/// <summary>Un referto da proteggere: file in <see cref="TempFileStore"/> e nome originale.</summary>
public sealed record ProtectionInput(string FilePath, string OriginalFileName);

public sealed record ProtectedPackage(string FilePath, ProtectionKind Kind, string Extension);

/// <summary>Il PDF è già protetto da password: non si può unire né ricifrare.</summary>
public sealed class ProtectedPdfException(string originalFileName)
    : Exception("Il PDF è già protetto da password.")
{
    public string OriginalFileName { get; } = originalFileName;
}

/// <summary>Il file ha estensione .pdf ma non è un PDF leggibile.</summary>
public sealed class InvalidPdfException(string originalFileName)
    : Exception("Il file PDF non è leggibile.")
{
    public string OriginalFileName { get; } = originalFileName;
}

/// <summary>
/// Protegge i referti con la password: un unico PDF cifrato AES-128 se sono tutti PDF, altrimenti uno ZIP AES-256.
/// Il risultato è scritto nella cartella indicata (quella temporanea dell'invio), mai accanto ai file originali.
/// </summary>
public sealed class AttachmentProtector
{
    private const int ZipCompressionLevel = 6;

    public static bool IsPdf(string fileName) =>
        string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<ProtectedPackage> ProtectAsync(
        IReadOnlyList<ProtectionInput> reports,
        string outputFolder,
        string password,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfZero(reports.Count);
        ArgumentException.ThrowIfNullOrEmpty(password);

        // PDFsharp e SharpZipLib sono sincroni: il lavoro va fuori dal thread del circuito Blazor.
        return Task.Run(
            () => reports.All(r => IsPdf(r.OriginalFileName))
                ? ProtectPdf(reports, outputFolder, password, ct)
                : ProtectZip(reports, outputFolder, password, ct),
            ct);
    }

    private static ProtectedPackage ProtectPdf(
        IReadOnlyList<ProtectionInput> reports, string outputFolder, string password, CancellationToken ct)
    {
        var outputPath = NewOutputPath(outputFolder, ".pdf");
        var sources = new List<PdfDocument>();
        try
        {
            using var output = new PdfDocument();
            foreach (var report in reports)
            {
                ct.ThrowIfCancellationRequested();
                var source = OpenForImport(report);
                sources.Add(source);
                foreach (var page in source.Pages)
                {
                    output.AddPage(page);
                }
            }

            var security = output.SecuritySettings;
            security.UserPassword = password;
            // La password del proprietario è casuale e non viene comunicata a nessuno.
            security.OwnerPassword = PasswordGenerator.Generate();
            // AES a 128 bit (PDF 1.6), impostato esplicitamente: un cambio del default di PDFsharp non deve modificarlo.
            // Mai RC4 (V1-V3), mai V5 (AES-256, PDF 2.0): alcuni lettori dei pazienti non la aprono.
            output.SecurityHandler.SetEncryption(PdfDefaultEncryption.V4UsingAES);
            security.PermitPrint = true;
            security.PermitFullQualityPrint = true;
            security.PermitExtractContent = true;
            security.PermitModifyDocument = false;
            security.PermitAnnotations = false;
            security.PermitFormsFill = false;
            security.PermitAssembleDocument = false;

            output.Save(outputPath);
        }
        catch
        {
            TryDelete(outputPath);
            throw;
        }
        finally
        {
            foreach (var source in sources)
            {
                source.Dispose();
            }
        }

        return new ProtectedPackage(outputPath, ProtectionKind.PdfAes, ".pdf");
    }

    private static PdfDocument OpenForImport(ProtectionInput report)
    {
        try
        {
            return PdfReader.Open(report.FilePath, PdfDocumentOpenMode.Import);
        }
        catch (PdfReaderException) when (HasEncryptionDictionary(report.FilePath))
        {
            // PDFsharp non apre un PDF che richiede la password: non si può unire né ricifrare.
            throw new ProtectedPdfException(report.OriginalFileName);
        }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException)
        {
            throw new InvalidPdfException(report.OriginalFileName);
        }
    }

    private static bool HasEncryptionDictionary(string path)
    {
        // /Encrypt è nel trailer (o nel dizionario del flusso di riferimenti), cioè in fondo al file.
        using var stream = File.OpenRead(path);
        const int tail = 64 * 1024;
        stream.Seek(Math.Max(0, stream.Length - tail), SeekOrigin.Begin);
        var buffer = new byte[Math.Min(stream.Length, tail)];
        stream.ReadExactly(buffer);
        return System.Text.Encoding.Latin1.GetString(buffer).Contains("/Encrypt", StringComparison.Ordinal);
    }

    private static ProtectedPackage ProtectZip(
        IReadOnlyList<ProtectionInput> reports, string outputFolder, string password, CancellationToken ct)
    {
        var outputPath = NewOutputPath(outputFolder, ".zip");
        try
        {
            using var file = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var zip = new ZipOutputStream(file) { Password = password };
            zip.SetLevel(ZipCompressionLevel);

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var report in reports)
            {
                ct.ThrowIfCancellationRequested();
                var info = new FileInfo(report.FilePath);
                var entry = new ZipEntry(ZipEntry.CleanName(UniqueEntryName(report.OriginalFileName, usedNames)))
                {
                    // Mai ZipCrypto (il default se non si imposta AESKeySize): ogni entry è AES-256.
                    AESKeySize = 256,
                    DateTime = DateTime.Now,
                    Size = info.Length,
                    IsUnicodeText = true,
                };
                zip.PutNextEntry(entry);
                using (var source = File.OpenRead(report.FilePath))
                {
                    source.CopyTo(zip);
                }
                zip.CloseEntry();
            }
            zip.Finish();
        }
        catch
        {
            TryDelete(outputPath);
            throw;
        }

        return new ProtectedPackage(outputPath, ProtectionKind.ZipAes, ".zip");
    }

    private static string UniqueEntryName(string originalFileName, HashSet<string> used)
    {
        var name = Path.GetFileName(originalFileName);
        if (name.Length == 0)
        {
            name = "documento";
        }
        var candidate = name;
        for (var i = 2; !used.Add(candidate); i++)
        {
            candidate = $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}";
        }
        return candidate;
    }

    private static string NewOutputPath(string folder, string extension)
    {
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, Guid.NewGuid().ToString("N") + extension);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Resta alla pulizia periodica della cartella.
        }
    }
}
