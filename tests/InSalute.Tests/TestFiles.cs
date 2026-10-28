using Microsoft.AspNetCore.Components.Forms;
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

/// <summary>
/// Restituisce i primi byte e poi non risponde più, ignorando anche la cancellazione: come la lettura di un file
/// dal browser quando la connessione cade a metà.
/// </summary>
public sealed class StalledStream(byte[] head) : Stream
{
    private bool _headSent;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!_headSent && head.Length > 0)
        {
            _headSent = true;
            head.CopyTo(buffer);
            return ValueTask.FromResult(head.Length);
        }
        return new ValueTask<int>(new TaskCompletionSource<int>().Task);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Un byte alla volta, con una pausa prima di ognuno: una connessione lenta ma attiva.</summary>
public sealed class TricklingStream(int chunks, TimeSpan delay) : Stream
{
    private int _sent;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_sent == chunks)
        {
            return 0;
        }
        await Task.Delay(delay, cancellationToken);
        buffer.Span[0] = (byte)_sent++;
        return 1;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>File scelto nel browser il cui contenuto arriva da uno stream qualsiasi (per esempio uno che si blocca).</summary>
public sealed class StreamBrowserFile(string name, long size, Func<Stream> open) : IBrowserFile
{
    public string Name => name;
    public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
    public long Size => size;
    public string ContentType => "application/octet-stream";

    public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) => open();
}
