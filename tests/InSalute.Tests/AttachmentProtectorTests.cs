using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using InSalute.Core.Dispatching;
using InSalute.Core.Domain;
using PdfSharp.Pdf.IO;
using Xunit;

namespace InSalute.Tests;

public class AttachmentProtectorTests : IDisposable
{
    private const string Password = "Ab3k9Qm2Xz7p";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "insalute-tests", Guid.NewGuid().ToString("N"));
    private readonly string _sources;
    private readonly string _output;
    private readonly AttachmentProtector _protector = new();

    public AttachmentProtectorTests()
    {
        _sources = Path.Combine(_folder, "sources");
        _output = Path.Combine(_folder, "out");
        Directory.CreateDirectory(_sources);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private ProtectionInput Source(string name, byte[] content)
    {
        var path = Path.Combine(_sources, Guid.NewGuid().ToString("N") + Path.GetExtension(name));
        File.WriteAllBytes(path, content);
        return new ProtectionInput(path, name);
    }

    [Fact]
    public async Task Single_pdf_opens_only_with_the_password_and_is_aes_128()
    {
        var package = await _protector.ProtectAsync([Source("referto.pdf", TestFiles.Pdf(2))], _output, Password);

        Assert.Equal(ProtectionKind.PdfAes, package.Kind);
        Assert.Equal(".pdf", Path.GetExtension(package.FilePath));

        Assert.ThrowsAny<PdfReaderException>(() => PdfReader.Open(package.FilePath, PdfDocumentOpenMode.Import));
        Assert.ThrowsAny<PdfReaderException>(() => PdfReader.Open(package.FilePath, "password-sbagliata", PdfDocumentOpenMode.Import));
        using (var document = PdfReader.Open(package.FilePath, Password, PdfDocumentOpenMode.Import))
        {
            Assert.Equal(2, document.PageCount);
        }

        // Cifratura V4 con crypt filter AESV2 (AES-128): mai RC4 (CFM /V2) né V5 (/AESV3).
        var raw = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(package.FilePath));
        Assert.Contains("/AESV2", raw);
        Assert.Contains("/V 4", raw);
        Assert.DoesNotContain("/AESV3", raw);
        Assert.DoesNotContain("/CFM /V2", raw);
    }

    [Fact]
    public async Task Several_pdfs_become_one_pdf_with_the_sum_of_the_pages_in_order()
    {
        var package = await _protector.ProtectAsync(
            [Source("a.pdf", TestFiles.Pdf(1)), Source("b.pdf", TestFiles.Pdf(3)), Source("c.pdf", TestFiles.Pdf(2))],
            _output,
            Password);

        Assert.Equal(ProtectionKind.PdfAes, package.Kind);
        Assert.Equal(6, TestFiles.PageCount(await File.ReadAllBytesAsync(package.FilePath), Password));
    }

    [Fact]
    public async Task Uppercase_pdf_extension_is_treated_as_pdf()
    {
        var package = await _protector.ProtectAsync([Source("REFERTO.PDF", TestFiles.Pdf(1))], _output, Password);

        Assert.Equal(ProtectionKind.PdfAes, package.Kind);
    }

    [Fact]
    public async Task Pdf_and_image_become_a_zip_that_opens_only_with_the_password_and_uses_aes_256()
    {
        var image = TestFiles.Image();
        var pdf = TestFiles.Pdf(1);
        var package = await _protector.ProtectAsync(
            [Source("referto.pdf", pdf), Source("radiografia.jpg", image)], _output, Password);

        Assert.Equal(ProtectionKind.ZipAes, package.Kind);
        Assert.Equal(".zip", Path.GetExtension(package.FilePath));

        using var withoutPassword = new ZipFile(package.FilePath);
        Assert.Equal(2, withoutPassword.Count);
        Assert.ThrowsAny<ZipException>(() => withoutPassword.GetInputStream(withoutPassword.GetEntry("radiografia.jpg")).ReadByte());

        using var wrongPassword = new ZipFile(package.FilePath) { Password = "password-sbagliata" };
        Assert.ThrowsAny<Exception>(() => ReadAll(wrongPassword, "radiografia.jpg"));

        using var zip = new ZipFile(package.FilePath) { Password = Password };
        foreach (ZipEntry entry in zip)
        {
            // Mai ZipCrypto: ogni entry deve essere AES-256.
            Assert.Equal(256, entry.AESKeySize);
        }
        Assert.Equal(image, ReadAll(zip, "radiografia.jpg"));
        Assert.Equal(pdf, ReadAll(zip, "referto.pdf"));
    }

    [Fact]
    public async Task Zip_keeps_entries_with_the_same_name_apart()
    {
        var package = await _protector.ProtectAsync(
            [Source("scan.jpg", TestFiles.Image()), Source("scan.jpg", TestFiles.Image())], _output, Password);

        using var zip = new ZipFile(package.FilePath) { Password = Password };
        Assert.NotNull(zip.GetEntry("scan.jpg"));
        Assert.NotNull(zip.GetEntry("scan (2).jpg"));
    }

    [Fact]
    public async Task Already_protected_pdf_throws_ProtectedPdfException_and_leaves_no_output()
    {
        var ex = await Assert.ThrowsAsync<ProtectedPdfException>(() => _protector.ProtectAsync(
            [Source("ok.pdf", TestFiles.Pdf(1)), Source("chiuso.pdf", TestFiles.ProtectedPdf("segreta"))], _output, Password));

        Assert.Equal("chiuso.pdf", ex.OriginalFileName);
        Assert.Empty(Directory.GetFiles(_output));
    }

    [Fact]
    public async Task Unreadable_pdf_throws_InvalidPdfException()
    {
        await Assert.ThrowsAsync<InvalidPdfException>(() => _protector.ProtectAsync(
            [Source("rotto.pdf", Encoding.ASCII.GetBytes("non sono un pdf"))], _output, Password));
    }

    /// <summary>
    /// PDF danneggiati su cui PDFsharp lancia eccezioni diverse da PdfReaderException (Exception generica,
    /// ArgumentOutOfRange) oppure li apre "con 0 pagine" e fallisce scorrendo le pagine (NullReference).
    /// </summary>
    public static TheoryData<string> DamagedPdfs => ["troncato", "solo-intestazione", "senza-root", "kids-rotto"];

    private static byte[] Damaged(string kind)
    {
        var pdf = TestFiles.Pdf(2);
        var text = Encoding.Latin1.GetString(pdf);
        return kind switch
        {
            "troncato" => pdf[..(pdf.Length / 2)],
            "solo-intestazione" => Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF"),
            "senza-root" => Encoding.Latin1.GetBytes(text.Replace("/Root", "/Rxxx")),
            "kids-rotto" => Encoding.Latin1.GetBytes(text.Replace("/Kids", "/Kxxx")),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    [Theory]
    [MemberData(nameof(DamagedPdfs))]
    public async Task Damaged_pdf_is_rejected_by_inspection_as_unreadable(string kind)
    {
        var input = Source("rotto.pdf", Damaged(kind));

        var ex = await Assert.ThrowsAsync<InvalidPdfException>(() => _protector.InspectPdfAsync(input.FilePath, input.OriginalFileName));
        Assert.Equal("rotto.pdf", ex.OriginalFileName);
    }

    [Theory]
    [MemberData(nameof(DamagedPdfs))]
    public async Task Damaged_pdf_is_rejected_by_protection_and_leaves_no_output(string kind)
    {
        await Assert.ThrowsAsync<InvalidPdfException>(() => _protector.ProtectAsync(
            [Source("ok.pdf", TestFiles.Pdf(1)), Source("rotto.pdf", Damaged(kind))], _output, Password));

        Assert.Empty(Directory.Exists(_output) ? Directory.GetFiles(_output) : []);
    }

    [Fact]
    public async Task Output_goes_to_the_given_folder_and_never_next_to_the_originals()
    {
        var input = Source("referto.pdf", TestFiles.Pdf(1));

        var package = await _protector.ProtectAsync([input], _output, Password);

        Assert.Equal(_output, Path.GetDirectoryName(package.FilePath));
        Assert.Equal([input.FilePath], Directory.GetFiles(_sources));
    }

    private static byte[] ReadAll(ZipFile zip, string entryName)
    {
        using var stream = zip.GetInputStream(zip.GetEntry(entryName));
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
