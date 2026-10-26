using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InSalute.Core.Dispatching;

/// <summary>File salvato in una cartella di invio. Il nome originale vive solo in memoria e nel Dispatch.</summary>
public sealed record StoredFile(string StoredName, string OriginalFileName, long SizeBytes, string Sha256);

/// <summary>
/// Cartella temporanea dei file in lavorazione: una sottocartella per invio, chiamata con il DraftId.
/// Sul disco i file hanno nome GUID + estensione.
/// </summary>
public sealed partial class TempFileStore(IOptions<StorageOptions> options, ILogger<TempFileStore> logger)
{
    private readonly string _root = Path.GetFullPath(options.Value.TempPath);

    [GeneratedRegex(@"^[0-9a-f]{32}(\.[a-z0-9]{1,10})?$")]
    private static partial Regex StoredNamePattern();

    public string GetDraftFolder(Guid draftId)
    {
        if (draftId == Guid.Empty)
        {
            throw new ArgumentException("DraftId non valido.", nameof(draftId));
        }
        return Path.Combine(_root, draftId.ToString("N"));
    }

    public async Task<StoredFile> SaveAsync(Guid draftId, string originalFileName, Stream content, CancellationToken ct = default)
    {
        var folder = GetDraftFolder(draftId);
        Directory.CreateDirectory(folder);

        var extension = SafeExtension(originalFileName);
        var storedName = Guid.NewGuid().ToString("N") + extension;
        var path = Path.Combine(folder, storedName);

        using var hash = SHA256.Create();
        long size = 0;
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var buffer = new byte[81920];
            int read;
            while ((read = await content.ReadAsync(buffer, ct)) > 0)
            {
                hash.TransformBlock(buffer, 0, read, null, 0);
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                size += read;
            }
            hash.TransformFinalBlock([], 0, 0);
        }
        catch
        {
            TryDeleteFile(path);
            throw;
        }

        return new StoredFile(storedName, originalFileName, size, Convert.ToHexString(hash.Hash!).ToLowerInvariant());
    }

    /// <summary>
    /// Percorso di un file dell'invio, o null se il nome non è uno di quelli generati da <see cref="SaveAsync"/>
    /// o il file non esiste. Il nome arriva dalla UI: non è mai usato com'è.
    /// </summary>
    public string? ResolveFile(Guid draftId, string storedName)
    {
        if (!StoredNamePattern().IsMatch(storedName))
        {
            return null;
        }
        var path = Path.Combine(GetDraftFolder(draftId), storedName);
        return File.Exists(path) ? path : null;
    }

    public void DeleteDraft(Guid draftId)
    {
        var folder = GetDraftFolder(draftId);
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nei log solo il DraftId. La pulizia periodica ci riprova.
            logger.LogWarning(ex, "Impossibile cancellare la cartella temporanea dell'invio {DispatchId}", draftId);
        }
    }

    /// <summary>Cancella le sottocartelle più vecchie di <paramref name="maxAge"/>. Restituisce quante ne ha tolte.</summary>
    public int DeleteOlderThan(TimeSpan maxAge, DateTime utcNow)
    {
        if (!Directory.Exists(_root))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var folder in Directory.EnumerateDirectories(_root))
        {
            if (utcNow - Directory.GetLastWriteTimeUtc(folder) <= maxAge)
            {
                continue;
            }
            try
            {
                Directory.Delete(folder, recursive: true);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Impossibile cancellare una cartella temporanea scaduta");
            }
        }
        return deleted;
    }

    private static string SafeExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        extension = new string(extension.Where(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9')).ToArray());
        return extension.Length is > 0 and <= 10 ? "." + extension : "";
    }

    private static void TryDeleteFile(string path)
    {
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
