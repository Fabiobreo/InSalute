using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Security;

namespace InSalute.Tests;

/// <summary>File finti per i test: nessun dato reale.</summary>
public static class TestFiles
{
    public static byte[] Pdf(int pages)
    {
        using var document = new PdfDocument();
        for (var i = 0; i < pages; i++)
        {
            document.AddPage();
        }
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    /// <summary>PDF con password utente: PDFsharp non lo apre senza password.</summary>
    public static byte[] ProtectedPdf(string password)
    {
        using var document = new PdfDocument();
        document.AddPage();
        document.SecuritySettings.UserPassword = password;
        document.SecuritySettings.OwnerPassword = password + "owner";
        document.SecurityHandler.SetEncryption(PdfDefaultEncryption.V4UsingAES);
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    public static byte[] Image() => [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    public static int PageCount(byte[] pdf, string? password = null)
    {
        using var stream = new MemoryStream(pdf);
        using var document = password is null
            ? PdfReader.Open(stream, PdfDocumentOpenMode.Import)
            : PdfReader.Open(stream, password, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }
}
